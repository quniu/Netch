﻿using System;
using System.IO;

namespace Netch.Utils
{
    public static class Logging
    {
        private const string LogFile = "logging\\application.log";

        /// <summary>
        ///     信息
        /// </summary>
        /// <param name="text">内容</param>
        public static void Info(string text)
        {
            File.AppendAllText(LogFile, $@"[{DateTime.Now}][INFO] {text}{Global.EOF}");
        }

        /// <summary>
        ///     信息
        /// </summary>
        /// <param name="text">内容</param>
        public static void Warning(string text)
        {
            File.AppendAllText(LogFile, $@"[{DateTime.Now}][WARNING] {text}{Global.EOF}");
        }

        /// <summary>
        ///     错误
        /// </summary>
        /// <param name="text">内容</param>
        public static void Error(string text)
        {
            File.AppendAllText(LogFile, $@"[{DateTime.Now}][ERROR] {text}{Global.EOF}");
        }
    }
}