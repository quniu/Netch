﻿using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Netch.Forms;
using Netch.Models;
using Netch.Utils;
using nfapinet;

namespace Netch.Controllers
{
    public class NFController
    {
        /// <summary>
        ///     流量变动事件
        /// </summary>
        public event BandwidthUpdateHandler OnBandwidthUpdated;

        /// <summary>
        ///     流量变动处理器
        /// </summary>
        /// <param name="upload">上传</param>
        /// <param name="download">下载</param>
        public delegate void BandwidthUpdateHandler(long upload, long download);

        /// <summary>
        ///     进程实例
        /// </summary>
        public Process Instance;

        /// <summary>
        ///     当前状态
        /// </summary>
        public State State = State.Waiting;

        // 生成驱动文件路径
        public string driverPath = string.Format("{0}\\drivers\\netfilter2.sys", Environment.SystemDirectory);

        /// <summary>
        ///		启动
        /// </summary>
        /// <param name="server">服务器</param>
        /// <param name="mode">模式</param>
        /// <param name="StopServiceAndRestart">先停止驱动服务再重新启动</param>
        /// <returns>是否成功</returns>
        public bool Start(Server server, Mode mode, bool StopServiceAndRestart)
        {
            if (!StopServiceAndRestart)
                MainForm.Instance.StatusText(i18N.Translate("Starting Redirector"));

            if (!File.Exists("bin\\Redirector.exe"))
            {
                return false;
            }

            // 检查驱动是否存在
            if (File.Exists(driverPath))
            {
                // 生成系统版本
                var version = $"{Environment.OSVersion.Version.Major.ToString()}.{Environment.OSVersion.Version.Minor.ToString()}";
                var driverName = "";

                switch (version)
                {
                    case "10.0":
                        driverName = "Win-10.sys";
                        break;
                    case "6.3":
                    case "6.2":
                        driverName = "Win-8.sys";
                        break;
                    case "6.1":
                    case "6.0":
                        driverName = "Win-7.sys";
                        break;
                    default:
                        Logging.Info($"不支持的系统版本：{version}");
                        return false;
                }

                // 检查驱动版本号
                var SystemfileVerInfo = FileVersionInfo.GetVersionInfo(driverPath);
                var BinFileVerInfo = FileVersionInfo.GetVersionInfo(string.Format("bin\\{0}", driverName));

                if (!SystemfileVerInfo.FileVersion.Equals(BinFileVerInfo.FileVersion))
                {
                    Logging.Info("开始更新驱动");
                    // 需要更新驱动
                    try
                    {
                        var service = new ServiceController("netfilter2");
                        if (service.Status == ServiceControllerStatus.Running)
                        {
                            service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped);
                        }
                        NFAPI.nf_unRegisterDriver("netfilter2");

                        //删除老驱动
                        File.Delete(driverPath);
                        if (!InstallDriver())
                            return false;

                        Logging.Info($"驱动更新完毕，当前驱动版本:{BinFileVerInfo.FileVersion}");
                    }
                    catch (Exception)
                    {
                        Logging.Info("更新驱动出错");
                    }

                }

            }
            else
            {
                if (!InstallDriver())
                {
                    return false;
                }
            }

            try
            {
                // 启动驱动服务
                var service = new ServiceController("netfilter2");
                if (service.Status == ServiceControllerStatus.Running && StopServiceAndRestart)
                {
                    // 防止其他程序占用 重置 NF 百万连接数限制
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped);
                    MainForm.Instance.StatusText(i18N.Translate("Starting netfilter2 Service"));
                    service.Start();
                }
                else if (service.Status == ServiceControllerStatus.Stopped)
                {
                    MainForm.Instance.StatusText(i18N.Translate("Starting netfilter2 Service"));
                    service.Start();
                }
            }
            catch (Exception e)
            {
                Logging.Info(e.ToString());

                var result = NFAPI.nf_registerDriver("netfilter2");
                if (result != NF_STATUS.NF_STATUS_SUCCESS)
                {
                    Logging.Info($"注册驱动失败，返回值：{result}");
                    return false;
                }
            }

            var processes = "";
            foreach (var proc in mode.Rule)
            {
                processes += proc;
                processes += ",";
            }
            processes += "NTT.exe";

            Instance = MainController.GetProcess();
            Instance.StartInfo.FileName = "bin\\Redirector.exe";
            Instance.StartInfo.Arguments = "";

            if (server.Type != "Socks5")
            {
                Instance.StartInfo.Arguments += $"-r 127.0.0.1:{Global.Settings.Socks5LocalPort} -p \"{processes}\"";
            }
            else
            {
                var result = DNS.Lookup(server.Hostname);
                if (result == null)
                {
                    Logging.Info("无法解析服务器 IP 地址");
                    return false;
                }

                Instance.StartInfo.Arguments += $"-r {result}:{server.Port} -p \"{processes}\"";
                if (!string.IsNullOrWhiteSpace(server.Username) && !string.IsNullOrWhiteSpace(server.Password))
                {
                    Instance.StartInfo.Arguments += $" -username \"{server.Username}\" -password \"{server.Password}\"";
                }
            }

            if (File.Exists("logging\\redirector.log"))
            {
                File.Delete("logging\\redirector.log"); 
            }

            Instance.StartInfo.Arguments += $" -t {Global.Settings.RedirectorTCPPort}";
            Logging.Info(Instance.StartInfo.Arguments);
            Instance.OutputDataReceived += OnOutputDataReceived;
            Instance.ErrorDataReceived += OnOutputDataReceived;
            State = State.Starting;
            Instance.Start();
            Instance.BeginOutputReadLine();
            Instance.BeginErrorReadLine();

            for (var i = 0; i < 10; i++)
            {
                Thread.Sleep(1000);

                if (State == State.Started)
                {
                    return true;
                }
            }

            Logging.Info("NF 进程启动超时");
            Stop();
            return false;
        }

        /// <summary>
        ///		停止
        /// </summary>
        public void Stop()
        {
            try
            {
                if (Instance != null && !Instance.HasExited)
                {
                    Instance.Kill();
                    Instance.WaitForExit();
                }
            }
            catch (Exception e)
            {
                Logging.Info(e.ToString());
            }
        }
        public bool InstallDriver()
        {

            Logging.Info("安装驱动中");
            // 生成系统版本
            var version = $"{Environment.OSVersion.Version.Major.ToString()}.{Environment.OSVersion.Version.Minor.ToString()}";

            // 检查系统版本并复制对应驱动
            try
            {
                switch (version)
                {
                    case "10.0":
                        File.Copy("bin\\Win-10.sys", driverPath);
                        Logging.Info("已复制 Win10 驱动");
                        break;
                    case "6.3":
                    case "6.2":
                        File.Copy("bin\\Win-8.sys", driverPath);
                        Logging.Info("已复制 Win8 驱动");
                        break;
                    case "6.1":
                    case "6.0":
                        File.Copy("bin\\Win-7.sys", driverPath);
                        Logging.Info("已复制 Win7 驱动");
                        break;
                    default:
                        Logging.Info($"不支持的系统版本：{version}");
                        return false;
                }
            }
            catch (Exception e)
            {
                Logging.Info("复制驱动文件失败");
                Logging.Info(e.ToString());
                return false;
            }
            MainForm.Instance.StatusText(i18N.Translate("Register driver"));
            // 注册驱动文件
            var result = NFAPI.nf_registerDriver("netfilter2");
            if (result != NF_STATUS.NF_STATUS_SUCCESS)
            {
                Logging.Info($"注册驱动失败，返回值：{result}");
                return false;
            }
            return true;
        }

        public void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                File.AppendAllText("logging\\redirector.log", string.Format("{0}\r\n", e.Data));

                if (State == State.Starting)
                {
                    if (Instance.HasExited)
                    {
                        State = State.Stopped;
                    }
                    else if (e.Data.Contains("Started"))
                    {
                        State = State.Started;
                    }
                    else if (e.Data.Contains("Failed") || e.Data.Contains("Unable"))
                    {
                        State = State.Stopped;
                    }
                }
                else if (State == State.Started)
                {
                    if (e.Data.StartsWith("[APP][Bandwidth]"))
                    {
                        var splited = e.Data.Replace("[APP][Bandwidth]", "").Trim().Split(',');
                        if (splited.Length == 2)
                        {
                            var uploadSplited = splited[0].Split(':');
                            var downloadSplited = splited[1].Split(':');

                            if (uploadSplited.Length == 2 && downloadSplited.Length == 2)
                            {
                                if (long.TryParse(uploadSplited[1], out var upload) && long.TryParse(downloadSplited[1], out var download))
                                {
                                    Task.Run(() => OnBandwidthUpdated(upload, download));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
