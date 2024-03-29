﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorRunner.Runner
{
    public class InvokableMember : RegisteredObject, IInvokableMember
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public MethodInfo BackingMethod { get; init; }

        public string Group { get; set; }

        public object[] DefaultParameters { get; set; } = null;

        public object BackingInstance { get; init; }

        public Guid Parent { get; set; }

        public bool AcceptsCancellationToken { get; set; } = false;

        public Action<CancellationToken> ToAction()
        {
            if (AcceptsCancellationToken)
            {
                return (CancellationToken token) => Invoke(token);
            }
            return (CancellationToken token) => Invoke();
        }

        public object Invoke()
        {
            return BackingMethod?.Invoke(BackingInstance, DefaultParameters);
        }

        public object Invoke(CancellationToken token)
        {
            if (AcceptsCancellationToken)
            {
                return Invoke(new object[] { token });
            }
            return Invoke();
        }
        public object Invoke(params object[] parameters)
        {
            return BackingMethod?.Invoke(BackingInstance, parameters.Length != 0 ? parameters : DefaultParameters);
        }

        public override string ToString()
        {
            return $"{Name ?? Id.ToString()}";
        }
    }
}
