using MediaInfo;
using System;
using System.Collections.Generic;
using System.Text;

namespace MediaInfoTest
{
    class ConsoleLogger : ILogger
    {
        public void Log(LogLevel loglevel, string message, params object[] parameters)
        {
            Console.WriteLine($"{loglevel}: {string.Format(message, parameters)}");
        }
        
    }
}
