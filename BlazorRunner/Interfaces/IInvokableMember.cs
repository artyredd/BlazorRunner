﻿using System;
using System.Reflection;

namespace BlazorRunner.Runner
{
    public interface IInvokableMember : IBasicInfo, IGrouped, IInstanced
    {
        Guid Parent { get; set; }
        MethodInfo BackingMethod { get; }

        object[] DefaultParameters { get; set; }

        object Invoke();

        object Invoke(params object[] parameters);
        Action ToAction();
    }
}