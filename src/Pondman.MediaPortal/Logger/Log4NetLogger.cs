﻿using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System;
using System.Text;
using MediaPortal.Configuration;
using MPProfile = MediaPortal.Profile;
using MPServices = MediaPortal.Services;

namespace Pondman.MediaPortal
{
    public class Log4NetLogger: ILogger
    {
        private readonly ILog Log;
        
        public Log4NetLogger(string name)
        {
            var loglevel = log4net.Core.Level.All;
            var mploglevel = (MPServices.Level)Enum.Parse(typeof(MPServices.Level), MPProfile.MPSettings.Instance.GetValueAsString("general", "loglevel", "2"));
            switch (mploglevel)
            {
                case MPServices.Level.Information: loglevel = log4net.Core.Level.Info; break;
                case MPServices.Level.Warning: loglevel = log4net.Core.Level.Warn; break;
                case MPServices.Level.Error: loglevel = log4net.Core.Level.Error; break;
            }
            
            var hierarchy = (Hierarchy)LogManager.CreateRepository(name);
            var patternLayout = new PatternLayout
            {
                ConversionPattern = "[%date{MM-dd HH:mm:ss,fff}] [%-12thread] [%-5level] %message%newline"
            };
            patternLayout.ActivateOptions();
            var roller = new RollingFileAppender
            {
                Encoding = Encoding.UTF8,
                Layout = patternLayout,
                LockingModel = new FileAppender.MinimalLock(),
                AppendToFile = true,
                RollingStyle = RollingFileAppender.RollingMode.Once,
                PreserveLogFileNameExtension = true,
                MaxSizeRollBackups = 1,
                MaximumFileSize = "10MB",
                StaticLogFileName = true,
                File = Config.GetFile(Config.Dir.Log, name + ".log")
            };
            roller.ActivateOptions();
            hierarchy.Root.AddAppender(roller);
            hierarchy.Root.Level = loglevel;
            hierarchy.Configured = true;

            Log = LogManager.GetLogger(name, typeof(Log4NetLogger));
        }

        public void Info(string format, params object[] args)
        {
            Log.InfoFormat(format, args);
        }

        public void Warn(string format, params object[] args)
        {
            Log.WarnFormat(format, args);
        }

        public void Debug(string format, params object[] args)
        {
            Log.DebugFormat(format, args);
        }

        public void Error(string format, params object[] args)
        {
            Log.ErrorFormat(format, args);
        }

        public void Error(Exception e)
        {
            Log.Error(e);
        }
    }
}
