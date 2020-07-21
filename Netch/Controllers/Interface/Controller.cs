﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Netch.Models;
using Netch.Utils;

namespace Netch.Controllers
{
    public abstract class Controller
    {
        /// <summary>
        ///     控制器名
        /// </summary>
        public string Name;

        /// <summary>
        ///     进程实例
        /// </summary>
        public Process Instance;

        /// <summary>
        ///     主程序名(不含扩展名)
        /// </summary>
        public string MainFile;

        private List<string> _startedKeywords = new List<string>();

        private List<string> _stoppedKeywords = new List<string>();

        protected bool RedirectStd = true;

        protected void StartedKeywords(params string[] texts)
        {
            foreach (var text in texts)
            {
                _startedKeywords.Add(text);
            }
        }


        protected void StoppedKeywords(params string[] texts)
        {
            foreach (var text in texts)
            {
                _stoppedKeywords.Add(text);
            }
        }


        /// <summary>
        ///     当前状态
        /// </summary>
        protected State State = State.Waiting;

        public abstract void Stop();

        /// <summary>
        ///     停止
        /// </summary>
        protected void StopInstance()
        {
            try
            {
                if (Instance == null || Instance.HasExited) return;
                Instance.Kill();
                Instance.WaitForExit();
            }
            catch (Exception e)
            {
                Logging.Error($"停止 {MainFile} 错误：\n" + e);
            }
        }


        public void ClearLog()
        {
            try
            {
                if (File.Exists($"logging\\{Name}.log")) File.Delete($"logging\\{Name}.log");
            }
            catch (Exception)
            {
                // ignored
            }
        }

        /// <summary>
        ///     写日志
        /// </summary>
        /// <param name="s"></param>
        /// <returns><see cref="s" />是否为空</returns>
        protected bool Write(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            try
            {
                File.AppendAllText($"logging\\{Name}.log", s + Global.EOF);
            }
            catch (Exception e)
            {
                Logging.Error($"写入{Name}日志错误：\n" + e);
            }

            return true;
        }

        public Process GetProcess()
        {
            var p = new Process
            {
                StartInfo =
                {
                    FileName = Path.GetFullPath($"bin\\{MainFile}"),
                    WorkingDirectory = $"{Global.NetchDir}\\bin",
                    CreateNoWindow = true,
                    RedirectStandardError = RedirectStd,
                    RedirectStandardInput = RedirectStd,
                    RedirectStandardOutput = RedirectStd,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };
            return p;
        }

        /// <summary>
        ///     接收输出数据
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">数据</param>
        protected void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            // 写入日志
            if (!Write(e.Data)) return;

            // 检查启动
            if (State == State.Starting)
            {
                if (Instance.HasExited)
                {
                    State = State.Stopped;

                    return;
                }

                foreach (var s in _startedKeywords)
                {
                    if (e.Data.Contains(s))
                    {
                        State = State.Started;

                        return;
                    }
                }

                foreach (var s in _stoppedKeywords)
                {
                    if (e.Data.Contains(s))
                    {
                        State = State.Stopped;

                        return;
                    }
                }
            }
        }
    }
}