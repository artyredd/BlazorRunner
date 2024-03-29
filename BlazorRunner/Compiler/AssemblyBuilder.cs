﻿using BlazorRunner.Attributes;
using BlazorRunner.Runner.RuntimeHandling;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorRunner.Runner
{
    public class AssemblyBuilder : IAssemblyBuilder
    {
        public static readonly object[] DefaultSliderMininums = {
            sbyte.MinValue,
            (byte)0,
            short.MinValue,
            (ushort)0,
            int.MinValue,
            (uint)0,
            long.MinValue,
            (ulong)0,
            -1f,
            -1d,
            -1m,
            char.MinValue
        };

        public static readonly object[] DefaultSliderMaximums = {
            sbyte.MaxValue,
            byte.MaxValue,
            short.MaxValue,
            ushort.MaxValue,
            int.MaxValue,
            uint.MaxValue,
            long.MaxValue,
            ulong.MaxValue,
            1f,
            1d,
            1m,
            char.MaxValue
        };

        public static readonly object[] DefaultSliderStepSizes = {
            (sbyte)1,
            (byte)1,
            (short)1,
            (ushort)1,
            (int)1,
            (uint)1,
            (long)1,
            (ulong)1,
            0.000001f,
            0.000001d,
            0.000001m,
            (char)1
        };

        public IScriptAssembly Parse(Assembly assembly)
        {
            if (assembly is null)
            {
                return null;
            }

            IScriptAssembly newScriptAssembly = Factory.CreateScriptAssembly();

            if (TryGetAttribute<NameAttribute>(assembly, out var att))
            {
                newScriptAssembly.Name = att.Name;
            }
            else
            {
                newScriptAssembly.Name = assembly.FullName.Split(',')[0];
            }
            if (TryGetAttribute(assembly, out DescriptionAttribute desc))
            {
                newScriptAssembly.Description = desc.Description;
            }

            // get all the types that have [Script]
            IEnumerable<Type> scripts = GetTypesWithAttribute<ScriptAttribute>(assembly);

            // iterate through the found scripts and parse them
            foreach (var item in scripts)
            {
                // create a new script object
                object ScriptInstance = CreateScriptInstance(item);

                // make sure the instance can interact with the UX
                ILogger injectedLogger = InjectILogger(ScriptInstance);

                IScript newScript = Factory.CreateScript(ScriptInstance);

                // make sure we have a way for the script to talk with the UX
                newScript.Logger = injectedLogger;

                // attach the flavor text like the name and description
                AssignFlavorText(newScript, item);

                // get the setup method
                AssignInvokableMember<SetupAttribute>(newScript, item, (script, obj) => script.Setup = obj);

                // assign the entry point if the script has one
                AssignInvokableMember<EntryPointAttribute>(newScript, item, (script, obj) => script.EntryPoint = obj);

                // assign the cleanup if it has one
                AssignInvokableMember<CleanupAttribute>(newScript, item, (script, obj) => script.Cleanup = obj);

                // assign the miniscripts
                newScript.MiniScripts = GetMethodsWithAttribute<MiniScriptAttribute>(item, ScriptInstance);

                // group the scripts
                newScript.ScriptGroups = GroupMiniScripts(newScript.MiniScripts);

                // get all of the settings for the script
                (newScript.Settings, newScript.SettingGroups) = GetScriptSettings(item, ScriptInstance);

                // make sure to add the object as a managed resource if the script implements IDisposable
                newScript.ManagedResource = CheckForManagedScript(item, ScriptInstance);

                // make sure we can find the script from any children invokable members
                AssignParents(newScript);

                newScriptAssembly.AddScript(newScript);
            }


            // if we didn't find any scripts check to see if the assembly is aconsole application
            // if it is just use it's entry point as a script
            if (scripts?.Count() is null or 0)
            {
                ParseGenericDLL(newScriptAssembly, assembly);
            }

            return newScriptAssembly;
        }

        private IScriptAssembly ParseGenericDLL(IScriptAssembly scriptAssembly, Assembly assembly)
        {
            var newScript = GenerateScriptFromConsoleApplication(assembly);

            if (newScript != null)
            {

                ILogger injectedLogger = InjectILogger(newScript);

                newScript.Logger = injectedLogger;

                scriptAssembly.AddScript(newScript);

                newScript.Name = scriptAssembly.Name;

                newScript.IsGenericDLL = true;

                scriptAssembly.IsGenericDLL = true;
            }

            return scriptAssembly;
        }

        private void AssignParents(IScript script)
        {
            foreach (var item in script.MiniScripts)
            {
                item.Parent = script.Id;
            }
            script.Setup.Parent = script.Id;
            script.EntryPoint.Parent = script.Id;
            script.Cleanup.Parent = script.Id;
        }

        private ILogger InjectILogger(object instance)
        {

            Type instanceType = instance.GetType();

            var fields = instanceType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            var loggerFields = fields.Where(x => x.FieldType == typeof(ILogger));

            Guid id = Guid.NewGuid();

            if (instance is RegisteredObject obj)
            {
                id = obj.Id;
            }

            string name = null;

            if (instance is IBasicInfo info)
            {
                name = info.Name;
            }

            foreach (var item in loggerFields)
            {
                if (TryGetAttribute<LoggerAttribute>(item, out _))
                {
                    var logger = LoggerDirector.CreateLogger(id, name);

                    item.SetValue(instance, logger);

                    return logger;
                }
            }

            var properties = instanceType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            var loggerProperties = properties.Where(x => x.PropertyType == typeof(ILogger));

            foreach (var item in loggerProperties)
            {
                if (TryGetAttribute<LoggerAttribute>(item, out _))
                {
                    var logger = LoggerDirector.CreateLogger(id, name);

                    item.SetValue(instance, logger);

                    return logger;
                }
            }

            return LoggerDirector.CreateLogger(id, name);
        }

        private bool TryGetAttribute<T>(MemberInfo type, out T attribute) where T : System.Attribute
        {
            attribute = type.GetCustomAttribute<T>();

            return attribute != null;
        }

        private bool TryGetAttribute<T>(Assembly type, out T attribute) where T : System.Attribute
        {
            if (type == null)
            {
                attribute = default;
                return false;
            }

            attribute = type.GetCustomAttribute<T>();

            return attribute != null;
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
                var newScript = Factory.CreateScript(null);

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

        private void AssignSliderProperties(IScriptSetting instancedSetting)
        {

            // get the information about the setting
            var (MemberInfo, _) = instancedSetting.GetBackingInformation();

            var RangeAttribute = MemberInfo.GetCustomAttribute<RangeAttribute>();

            object Instance = instancedSetting.Value;

            if (Instance is null)
            {
                return;
            }

            if (instancedSetting is ISlider slider)
            {
                // get the defaults
                int index = Array.IndexOf(DefaultSliderMininums, DefaultSliderMininums.Where(x => x.GetType() == Instance.GetType()).FirstOrDefault());

                if (index != -1)
                {
                    slider.Min = DefaultSliderMininums[index];
                    slider.Max = DefaultSliderMaximums[index];
                    slider.StepSize = DefaultSliderStepSizes[index];
                }
            }

            if (RangeAttribute is null)
            {
                return;
            }

            // there is a possibility they are null make sure they are not
            if (Instance != null && MemberInfo != null)
            {
                // make sure the type of instance is an eligible slider type(aka a primitive)
                if (TypeValidator.TryGetEligibleType(Instance, out Type EligibleType, ValidatorTypes.EligibleSliders))
                {
                    // verify that the limits that were provided in the attribute are the same type or at least converitble to
                    // the original type of the object since it will need to be constantly compared to the object
                    if (TypeValidator.TryGetCompatibility(RangeAttribute.Min, EligibleType, out var compatibility))
                    {
                        if (compatibility != CastingCompatibility.Implicit)
                        {
                            RangeAttribute.Min = TypeValidator.Cast(RangeAttribute.Min, EligibleType, compatibility);
                        }
                    }
                    else
                    {
                        if (RangeAttribute.Min is null && EligibleType.IsValueType)
                        {
                            RangeAttribute.Min = Activator.CreateInstance(EligibleType);
                        }
                        else
                        {
                            throw Helpers.Exceptions.IncompatibleTypeUsedWithRange(RangeAttribute.Min.GetType(), EligibleType);
                        }
                    }

                    if (TypeValidator.TryGetCompatibility(RangeAttribute.Max, EligibleType, out compatibility))
                    {
                        if (compatibility != CastingCompatibility.Implicit)
                        {
                            RangeAttribute.Max = TypeValidator.Cast(RangeAttribute.Max, EligibleType, compatibility);
                        }
                    }
                    else
                    {
                        if (RangeAttribute.Max is null && EligibleType.IsValueType)
                        {
                            var max = TypeValidator.DefaultMaximums.Where(x => x.GetType() == EligibleType).FirstOrDefault();

                            if (max != null)
                            {
                                RangeAttribute.Max = max;
                            }
                            else
                            {
                                RangeAttribute.Max = Activator.CreateInstance(EligibleType);
                            }
                        }
                        else
                        {
                            throw Helpers.Exceptions.IncompatibleTypeUsedWithRange(RangeAttribute.Min.GetType(), EligibleType);
                        }
                    }

                    if (TypeValidator.TryGetCompatibility(RangeAttribute.StepAmount, EligibleType, out compatibility))
                    {
                        if (compatibility != CastingCompatibility.Implicit)
                        {
                            RangeAttribute.StepAmount = TypeValidator.Cast(RangeAttribute.StepAmount, EligibleType, compatibility);
                        }
                    }
                    else
                    {
                        RangeAttribute.StepAmount = GetDefaultSliderValue(EligibleType);
                    }

                    if (instancedSetting is ISlider slider1)
                    {
                        slider1.Min = RangeAttribute.Min;
                        slider1.Max = RangeAttribute.Max;
                        slider1.StepSize = RangeAttribute.StepAmount;
                        slider1.SliderCompatible = true;
                    }
                }
            }
        }

        private object GetDefaultSliderValue(Type DesiredType)
        {
            object defaultVal = DefaultSliderStepSizes.Where(x => x.GetType() == DesiredType).FirstOrDefault();
            if (defaultVal != null)
            {
                return defaultVal;
            }

            return 1;
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

                AssignSliderProperties(setting);

                settings.Add(setting);
            }

            foreach (FieldInfo item in fieldSettings)
            {
                IScriptSetting setting = Factory.CreateScriptSetting(item, BackingInstance);

                AssignFlavorText(setting, item);

                string group = AssignGroup(setting, item) ?? "Other";

                Helpers.Dictionary.Append(groupDict, group, setting);

                AssignSliderProperties(setting);

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
                // allow scripts that have a cancellation token parameter
                var parameters = method.GetParameters();

                bool acceptsCancellationToken = false;

                if (parameters.Length > 0)
                {
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(CancellationToken))
                    {
                        acceptsCancellationToken = true;
                    }
                    else
                    {
                        throw Helpers.Exceptions.IncompatibleWithParameters(method);
                    }
                }

                // create a new invokable member
                IInvokableMember newMember = Factory.CreateInvokableMember(method, backingInstance);

                newMember.AcceptsCancellationToken = acceptsCancellationToken;

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
            if (Helpers.Attributes.TryAssignProperties<NameAttribute>(type, script) is false)
            {
                if (script is IBasicInfo info)
                {
                    info.Name = type.Name;
                }
            }

            Helpers.Attributes.TryAssignProperties<DescriptionAttribute>(type, script);
        }

        private string AssignGroup(object possibleGroupedObject, MemberInfo type)
        {
            ISupportsGrouping groupAttr = type.GetCustomAttribute<SettingAttribute>();

            if (groupAttr is null)
            {
                groupAttr = type.GetCustomAttribute<MiniScriptAttribute>();
            }

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

        private IEnumerable<Type> GetTypesWithAttribute<T>(Assembly assembly) where T : Attribute
        {
            return assembly.GetTypes().Where(x => x.GetCustomAttributes<T>().Any());
        }
    }
}
