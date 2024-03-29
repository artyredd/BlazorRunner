﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlazorRunner.Runner.RuntimeHandling
{
    /// <summary>
    /// Redirects and splits logs sent to this <see cref="ILogger"/> to all <see cref="ILogger"/>s provided to it's constructor.
    /// </summary>
    public class LoggerSplitter : ILogger
    {
        public readonly ILogger[] Loggers = Array.Empty<ILogger>();

        [DebuggerHidden]
        public LoggerSplitter(params ILogger[] loggers)
        {
            Loggers = loggers;
        }

        [DebuggerHidden]
        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }

        [DebuggerHidden]
        public bool IsEnabled(LogLevel logLevel)
        {
            foreach (var item in Loggers)
            {
                if (item?.IsEnabled(logLevel) is false)
                {
                    return false;
                }
            }
            return true;
        }

        [DebuggerHidden]
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            foreach (var item in Loggers)
            {
                item?.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
