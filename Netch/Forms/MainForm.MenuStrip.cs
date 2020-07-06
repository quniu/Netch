﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;
using Netch.Controllers;
using Netch.Forms.Mode;
using Netch.Forms.Server;
using Netch.Models;
using Netch.Utils;
using nfapinet;
using Trojan = Netch.Forms.Server.Trojan;
using VMess = Netch.Forms.Server.VMess;
using WebClient = Netch.Override.WebClient;

namespace Netch.Forms
{
    partial class Dummy
    {}
    partial class MainForm
    {
        #region MenuStrip

        #region 服务器

        private void ImportServersFromClipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var texts = Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(texts))
            {
                var result = ShareLink.Parse(texts);

                if (result != null)
                {
                    Global.Settings.Server.AddRange(result);
                }
                else
                {
                    MessageBox.Show(i18N.Translate("Import servers error!"), i18N.Translate("Error"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }

                InitServer();
                Configuration.Save();
            }
        }

        private void AddSocks5ServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new Socks5().Show();
            Hide();
        }

        private void AddShadowsocksServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new Shadowsocks().Show();
            Hide();
        }

        private void AddShadowsocksRServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new ShadowsocksR().Show();
            Hide();
        }

        private void AddVMessServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new VMess().Show();
            Hide();
        }

        private void AddTrojanServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new Trojan().Show();
            Hide();
        }

        #endregion

        #region 模式

        private void CreateProcessModeToolStripButton_Click(object sender, EventArgs e)
        {
            new Process().Show();
            Hide();
        }

        #endregion

        #region 订阅

        private void ManageSubscribeLinksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new SubscribeForm().Show();
            Hide();
        }

        private void UpdateServersFromSubscribeLinksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Global.Settings.UseProxyToUpdateSubscription && ServerComboBox.SelectedIndex == -1)
                Global.Settings.UseProxyToUpdateSubscription = false;

            if (Global.Settings.UseProxyToUpdateSubscription)
            {
                // 当前 ServerComboBox 中至少有一项
                if (ServerComboBox.SelectedIndex == -1)
                {
                    MessageBox.Show(i18N.Translate("Please select a server first"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                MenuStrip.Enabled = ConfigurationGroupBox.Enabled = ControlButton.Enabled = SettingsButton.Enabled = false;
                ControlButton.Text = "...";
            }

            if (Global.Settings.SubscribeLink.Count > 0)
            {
                StatusText(i18N.Translate("Starting update subscription"));
                DeleteServerPictureBox.Enabled = false;

                UpdateServersFromSubscribeLinksToolStripMenuItem.Enabled = false;
                Task.Run(() =>
                {
                    if (Global.Settings.UseProxyToUpdateSubscription)
                    {
                        var mode = new Models.Mode
                        {
                            Remark = "ProxyUpdate",
                            Type = 5
                        };
                        MainController = new MainController();
                        MainController.Start(ServerComboBox.SelectedItem as Models.Server, mode);
                    }

                    foreach (var item in Global.Settings.SubscribeLink)
                    {
                        using var client = new WebClient();
                        try
                        {
                            if (!string.IsNullOrEmpty(item.UserAgent))
                            {
                                client.Headers.Add("User-Agent", item.UserAgent);
                            }
                            else
                            {
                                client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/77.0.3865.90 Safari/537.36");
                            }

                            if (Global.Settings.UseProxyToUpdateSubscription)
                            {
                                client.Proxy = new WebProxy($"http://127.0.0.1:{Global.Settings.HTTPLocalPort}");
                            }

                            var response = client.DownloadString(item.Link);

                            try
                            {
                                response = ShareLink.URLSafeBase64Decode(response);
                            }
                            catch (Exception)
                            {
                                // 跳过
                            }

                            Global.Settings.Server = Global.Settings.Server.Where(server => server.Group != item.Remark).ToList();
                            var result = ShareLink.Parse(response);

                            if (result != null)
                            {
                                foreach (var x in result)
                                {
                                    x.Group = item.Remark;
                                }

                                Global.Settings.Server.AddRange(result);
                                NotifyIcon.ShowBalloonTip(5,
                                    UpdateChecker.Name,
                                    string.Format(i18N.Translate("Update {1} server(s) from {0}"), item.Remark, result.Count),
                                    ToolTipIcon.Info);
                            }
                            else
                            {
                                NotifyIcon.ShowBalloonTip(5,
                                    UpdateChecker.Name,
                                    string.Format(i18N.Translate("Update servers error from {0}"), item.Remark),
                                    ToolTipIcon.Error);
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }

                    InitServer();
                    DeleteServerPictureBox.Enabled = true;
                    if (Global.Settings.UseProxyToUpdateSubscription)
                    {
                        MenuStrip.Enabled = ConfigurationGroupBox.Enabled = ControlButton.Enabled = SettingsButton.Enabled = true;
                        ControlButton.Text = i18N.Translate("Start");
                        MainController.Stop();
                        NatTypeStatusLabel.Text = "";
                    }

                    Configuration.Save();
                    StatusText(i18N.Translate("Subscription updated"));
                }).ContinueWith(task => { BeginInvoke(new Action(() => { UpdateServersFromSubscribeLinksToolStripMenuItem.Enabled = true; })); });

                NotifyIcon.ShowBalloonTip(5,
                    UpdateChecker.Name,
                    i18N.Translate("Updating in the background"),
                    ToolTipIcon.Info);
            }
            else
            {
                MessageBox.Show(i18N.Translate("No subscription link"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        #endregion

        #region 选项

        private void RestartServiceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Enabled = false;
            StatusText(i18N.Translate("Restarting service"));

            Task.Run(() =>
            {
                try
                {
                    var service = new ServiceController("netfilter2");
                    if (service.Status == ServiceControllerStatus.Stopped)
                    {
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running);
                    }
                    else if (service.Status == ServiceControllerStatus.Running)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped);
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running);
                    }
                }
                catch (Exception)
                {
                    NFAPI.nf_registerDriver("netfilter2");
                }

                MessageBox.Show(this, i18N.Translate("Service has been restarted"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                Enabled = true;
            });
        }

        private void UninstallServiceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Enabled = false;
            StatusText(i18N.Translate("Uninstalling Service"));

            Task.Run(() =>
            {
                var driver = $"{Environment.SystemDirectory}\\drivers\\netfilter2.sys";
                if (File.Exists(driver))
                {
                    try
                    {
                        var service = new ServiceController("netfilter2");
                        if (service.Status == ServiceControllerStatus.Running)
                        {
                            service.Stop();
                            service.WaitForStatus(ServiceControllerStatus.Stopped);
                        }
                    }
                    catch (Exception)
                    {
                        // 跳过
                    }

                    try
                    {
                        NFAPI.nf_unRegisterDriver("netfilter2");

                        File.Delete(driver);

                        MessageBox.Show(this, i18N.Translate("Service has been uninstalled"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, i18N.Translate("Error") + i18N.Translate(": ") + ex, i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    MessageBox.Show(this, i18N.Translate("Service has been uninstalled"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                Enabled = true;
            });
        }

        private void ReloadModesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Enabled = false;
            SaveConfigs();
            Task.Run(() =>
            {
                InitMode();

                MessageBox.Show(this, i18N.Translate("Modes have been reload"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                Enabled = true;
            });
        }

        private void CleanDNSCacheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Enabled = false;
            Task.Run(() =>
            {
                DNS.Cache.Clear();

                MessageBox.Show(this, i18N.Translate("DNS cache cleanup succeeded"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                StatusText(i18N.Translate("DNS cache cleanup succeeded"));
                Enabled = true;
            });
        }

        private void OpenDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Utils.Utils.OpenDir(@".\");
        }

        private void reinstallTapDriverToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Task.Run(() =>
            {
                StatusText(i18N.Translate("Reinstalling TUN/TAP driver"));
                Enabled = false;
                try
                {
                    Configuration.deltapall();
                    Configuration.addtap();
                    NotifyIcon.ShowBalloonTip(5,
                        UpdateChecker.Name, i18N.Translate("Reinstall TUN/TAP driver successfully"),
                        ToolTipIcon.Info);
                }
                catch
                {
                    NotifyIcon.ShowBalloonTip(5,
                        UpdateChecker.Name, i18N.Translate("Reinstall TUN/TAP driver failed"),
                        ToolTipIcon.Error);
                }
                finally
                {
                    UpdateStatus(State.Waiting);
                    Enabled = true;
                }
            });
        }

        private void updateACLWithProxyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            updateACLWithProxyToolStripMenuItem.Enabled = false;

            // 当前 ServerComboBox 中至少有一项
            if (ServerComboBox.SelectedIndex == -1)
            {
                MessageBox.Show(i18N.Translate("Please select a server first"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MenuStrip.Enabled = ConfigurationGroupBox.Enabled = ControlButton.Enabled = SettingsButton.Enabled = false;
            ControlButton.Text = "...";


            Task.Run(() =>
            {
                var mode = new Models.Mode
                {
                    Remark = "ProxyUpdate",
                    Type = 5
                };
                MainController = new MainController();
                MainController.Start(ServerComboBox.SelectedItem as Models.Server, mode);

                using var client = new WebClient();

                client.Proxy = new WebProxy($"http://127.0.0.1:{Global.Settings.HTTPLocalPort}");

                StatusText(i18N.Translate("Updating in the background"));
                try
                {
                    client.DownloadFile(Global.Settings.ACL, "bin\\default.acl");
                    NotifyIcon.ShowBalloonTip(5,
                        UpdateChecker.Name, i18N.Translate("ACL updated successfully"),
                        ToolTipIcon.Info);
                }
                catch (Exception e)
                {
                    Logging.Info("使用代理更新 ACL 失败！" + e.Message);
                    MessageBox.Show(i18N.Translate("ACL update failed") + "\n" + e.Message);
                }
                finally
                {

                    UpdateStatus(State.Waiting);
                    MainController.Stop();
                }
            });
        }

        #endregion


        private void ExitToolStripButton_Click(object sender, EventArgs e)
        {
            // 已启动
            if (State != State.Waiting && State != State.Stopped)
            {
                // 未开启自动停止
                if (!Global.Settings.StopWhenExited)
                {
                    MessageBox.Show(i18N.Translate("Please press Stop button first"), i18N.Translate("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);

                    Visible = true;
                    ShowInTaskbar = true; // 显示在系统任务栏 
                    WindowState = FormWindowState.Normal; // 还原窗体 
                    NotifyIcon.Visible = true; // 托盘图标隐藏 

                    return;
                }
                // 自动停止

                ControlButton_Click(sender, e);
            }

            SaveConfigs();

            UpdateStatus(State.Terminating);
            NotifyIcon.Visible = false;
            Close();
            Dispose();
            Environment.Exit(Environment.ExitCode);
        }

        private void RelyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://mega.nz/file/9OQ1EazJ#0pjJ3xt57AVLr29vYEEv15GSACtXVQOGlEOPpi_2Ico");
        }

        private void VersionLabel_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start($"https://github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}/releases");
        }

        private void AboutToolStripButton_Click(object sender, EventArgs e)
        {
            new AboutForm().Show();
            Hide();
        }

        private void updateACLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StatusText(i18N.Translate("Starting update ACL"));
            using var client = new WebClient();

            client.DownloadFileTaskAsync(Global.Settings.ACL, "bin\\default.acl");
            client.DownloadFileCompleted += ((sender, args) =>
            {
                try
                {
                    if (args.Error == null)
                    {
                        NotifyIcon.ShowBalloonTip(5,
                            UpdateChecker.Name, i18N.Translate("ACL updated successfully"),
                            ToolTipIcon.Info);
                    }
                    else
                    {
                        Logging.Info("ACL 更新失败！" + args.Error);
                        MessageBox.Show(i18N.Translate("ACL update failed") + "\n" + args.Error);
                    }
                }
                finally
                {
                    UpdateStatus(State.Waiting);
                }
            });
        }

        #endregion

    }
}