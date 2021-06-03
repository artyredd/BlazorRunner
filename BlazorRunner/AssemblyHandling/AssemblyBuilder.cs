﻿using BlazorRunner.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BlazorRunner.Runner
{
    public class AssemblyBuilder : IAssemblyBuilder
    {
        public IScriptAssembly Parse(Assembly assembly)
        {
            IScriptAssembly newScriptAssembly = Factory.CreateScriptAssembly();

            // get all the types that have [Script]
            IEnumerable<Type> scripts = GetTypesWithAttribute<ScriptAttribute>(assembly);

            // iterate through the found scripts and parse them
            foreach (var item in scripts)
            {
                // create a new script object
                IScript newScript = Factory.CreateScript();

                // attach the flavor text like the name and description
                AssignFlavorText(newScript, item);

                // attempt to verify that the script has a public parameterless constructor then create an instance of the script
                newScript.BackingInstance = CreateScriptInstance(item);

                // get the setup method
                AssignInvokableMember<SetupAttribute>(newScript, item, (script, obj) => script.Setup = obj);

                // assign the entry point if the script has one
                AssignInvokableMember<EntryPointAttribute>(newScript, item, (script, obj) => script.EntryPoint = obj);

                // assign the cleanup if it has one
                AssignInvokableMember<CleanupAttribute>(newScript, item, (script, obj) => script.Cleanup = obj);

                // assign the miniscripts
                newScript.MiniScripts = GetMethodsWithAttribute<MiniScriptAttribute>(item, newScript.BackingInstance);

                // group the scripts
                newScript.ScriptGroups = GroupMiniScripts(newScript.MiniScripts);

                // get all of the settings for the script
                (newScript.Settings, newScript.SettingGroups) = GetScriptSettings(item, newScript.BackingInstance);

                // make sure to add the object as a managed resource if the script implements IDisposable
                newScript.ManagedResource = CheckForManagedScript(item, newScript.BackingInstance);

                newScriptAssembly.AddScript(newScript);
            }


            // if we didn't find any scripts check to see if the assembly is aconsole application
            // if it is just use it's entry point as a script
            if (scripts?.Count() is null or 0)
            {
                var newScript = GenerateScriptFromConsoleApplication(assembly);

                if (newScript != null)
                {
                    newScriptAssembly.AddScript(newScript);
                }
            }

            return newScriptAssembly;
        }

        private IScript GenerateScriptFromConsoleApplication(Assembly assembly)
        {
            // if there is no scripts in the assembly see if there is an actual entry point in the assembly
            // AKA a console application, we can consider any console application just a script with no settings or invokable
            // members
            var entryPoint = assembly.EntryPoint;

            if (entryPoint != null)
            {
                // create a new script
                var newScript = Factory.CreateScript();

                newScript.Name = assembly.FullName;

                // create an invokable member from the entry point of the provided program
                IInvokableMember main = Factory.CreateInvokableMember(entryPoint, null);

                // assign it back to the script
                newScript.EntryPoint = main;

                // make sure that the entry point does not require parameters
                var requiredParams = entryPoint.GetParameters();

                // if no parameters are required just return the new script
                if (requiredParams?.Length is not null and not 0)
                {
                    // create params for the most likely entry point signature Main(string[] args)
                    object[] likelyParams = { Array.Empty<string>() };

                    // ensure the signature matches
                    if (requiredParams?.FirstOrDefault().ParameterType is not null &&
                        requiredParams.FirstOrDefault()?.ParameterType == likelyParams[0].GetType())
                    {
                        main.DefaultParameters = likelyParams;
                    }
                    else
                    {
                        throw Helpers.Exceptions.IncompatibleEntryPoint(entryPoint);
                    }
                }

                return newScript;
            }

            return null;
        }

        private IDisposable CheckForManagedScript(Type scriptType, object BackingInstance)
        {
            if (scriptType.GetInterfaces().Contains(typeof(IDisposable)))
            {
                if (BackingInstance is IDisposable disposable)
                {
                    return disposable;
                }
            }

            return null;
        }

        private (IScriptSetting[], IDictionary<string, IScriptSetting[]>) GetScriptSettings(Type scriptType, object BackingInstance)
        {
            // we dont mind if they don't explictly chose a field or property, if it has a setting attribute select it
            var propertySettings = scriptType.GetProperties(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.Static).Where(x => x.GetCustomAttributes<SettingAttribute>().Any());

            var fieldSettings = scriptType.GetFields(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.Static).Where(x => x.GetCustomAttributes<SettingAttribute>().Any());

            List<IScriptSetting> settings = new();

            IDictionary<string, IScriptSetting[]> groupDict = new Dictionary<string, IScriptSetting[]>();

            // i could have joined these but honestly this was easier

            foreach (PropertyInfo item in propertySettings)
            {
                IScriptSetting setting = Factory.CreateScriptSetting(item, BackingInstance);

                AssignFlavorText(setting, item);

                string group = AssignGroup(setting, item) ?? "Other";

                Helpers.Dictionary.Append(groupDict, group, setting);

                settings.Add(setting);
            }

            foreach (FieldInfo item in fieldSettings)
            {
                IScriptSetting setting = Factory.CreateScriptSetting(item, BackingInstance);

                AssignFlavorText(setting, item);

                string group = AssignGroup(setting, item) ?? "Other";

                Helpers.Dictionary.Append(groupDict, group, setting);

                settings.Add(setting);
            }

            return (settings.ToArray(), groupDict);
        }

        private void AssignInvokableMember<T>(IScript script, Type scriptType, Action<IScript, IInvokableMember> Expression) where T : Attribute
        {
            // get the setup method
            var invokableMembers = GetMethodsWithAttribute<T>(scriptType, script.BackingInstance);

            if (invokableMembers.Length > 0)
            {
                if (invokableMembers.Length > 1)
                {
                    Helpers.Warnings.MultipleMethodsWithAttribute(typeof(SetupAttribute), script.Name);
                }

                Expression(script, invokableMembers[0]);
            }
        }

        private IInvokableMember[] GetMethodsWithAttribute<T>(Type scriptType, object backingInstance) where T : Attribute
        {
            Type attributeType = typeof(T);

            // we want the methods to be pretty flexivle in privacy since it removes biolerplate "public" from everything
            MethodInfo[] methods = scriptType.GetMethods(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.Instance
            ).Where(x => x.GetCustomAttributes<T>().Any()).ToArray();

            List<IInvokableMember> members = new();

            foreach (var method in methods)
            {
                // prevent generic methods
                if (method.ContainsGenericParameters | method.IsGenericMethodDefinition)
                {
                    throw Helpers.Exceptions.IncompatibleWithGenericMethods(method);
                }

                // prevent methods with parameters
                if (method.GetParameters().Length > 0)
                {
                    throw Helpers.Exceptions.IncompatibleWithParameters(method);
                }

                // create a new invokable member
                IInvokableMember newMember = Factory.CreateInvokableMember(method, backingInstance);

                // add some flavor text if it has it
                AssignFlavorText(newMember, method);

                AssignGroup(newMember, method);

                members.Add(newMember);
            }

            return members.ToArray();
        }

        private IDictionary<string, IInvokableMember[]> GroupMiniScripts(IEnumerable<IInvokableMember> Members)
        {
            if (Members == null)
            {
                return new Dictionary<string, IInvokableMember[]>();
            }

            return Members.GroupBy(x => x.Group, x => x).ToDictionary(x => x.Key, x => x.ToArray());
        }

        private void AssignFlavorText(object script, MemberInfo type)
        {

            // determine if the type was labeled with optional flavor text
            IDictionary<string, string> labels = GetFlavorText(type);

            if (labels.Count > 0)
            {
                if (script is IBasicInfo info)
                {
                    if (labels.ContainsKey("Name"))
                    {
                        info.Name = labels["Name"];
                    }
                    if (labels.ContainsKey("Description"))
                    {
                        info.Description = labels["Description"];
                    }
                }
            }
        }

        private string AssignGroup(object possibleGroupedObject, MemberInfo type)
        {
            var groupAttr = type.GetCustomAttribute<SettingAttribute>();

            if (possibleGroupedObject is IGrouped groupedObject)
            {
                groupedObject.Group = groupAttr?.Group ?? "Other";
            }

            return groupAttr?.Group ?? "Other";
        }

        object CreateScriptInstance(Type scriptType)
        {
            // make sure that the script has a parameterless public construtor
            if (scriptType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Where(x => x.GetParameters().Any() is false).Any() is false)
            {
                throw Helpers.Exceptions.NoParameterlessConstructor(scriptType);
            }

            // make sure that we don't break with generics
            if (scriptType.IsGenericType | scriptType.IsGenericTypeDefinition)
            {
                throw Helpers.Exceptions.IncompatibleWithGenericClasses(scriptType);
            }

            return Activator.CreateInstance(scriptType);
        }

        IDictionary<string, string> GetFlavorText(MemberInfo type)
        {
            var flavorText = new Dictionary<string, string>();

            flavorText.Add("Name", GetFlavorAttribute<NameAttribute>(type) ?? type.Name);

            string description = GetFlavorAttribute<DescriptionAttribute>(type);

            if (description != null)
            {
                flavorText.Add("Description", description);
            }

            return flavorText;
        }

        private string GetFlavorAttribute<T>(MemberInfo type) where T : Attribute
        {
            T attribute = type.GetCustomAttribute<T>();

            if (attribute != null)
            {
                Type attributeType = attribute.GetType();

                string propertyName = attributeType.Name.Split("Attribute")[0];

                return (string)attribute.GetType().GetProperty(propertyName).GetValue(attribute);
            }

            return null;
        }

        private IEnumerable<Type> GetTypesWithAttribute<T>(Assembly assembly) where T : Attribute
        {
            return assembly.GetTypes().Where(x => x.GetCustomAttributes<T>().Any());
        }
    }
}
