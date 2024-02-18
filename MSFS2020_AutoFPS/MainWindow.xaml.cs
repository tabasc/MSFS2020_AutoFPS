﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Threading;
using static System.Net.WebRequestMethods;

namespace MSFS2020_AutoFPS
{
    public partial class MainWindow : Window
    {
        protected NotifyIconViewModel notifyModel;
        protected ServiceModel serviceModel;
        protected DispatcherTimer timer;

        protected int editPairTLOD = -1;
        protected int editPairOLOD = -1;

        public MainWindow(NotifyIconViewModel notifyModel, ServiceModel serviceModel)
        {
            InitializeComponent();
            this.notifyModel = notifyModel;
            this.serviceModel = serviceModel;

             string assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            assemblyVersion = assemblyVersion[0..assemblyVersion.LastIndexOf('.')];
            Title += " (" + assemblyVersion + (serviceModel.TestVersion ? "-concept_demo" : "")+ ")";

            if (serviceModel.UseExpertOptions) stkpnlMSFSSettings.Visibility = Visibility.Visible;
            else stkpnlMSFSSettings.Visibility = Visibility.Collapsed;

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += OnTick;

            string latestAppVersionStr = GetFinalRedirect("https://github.com/ResetXPDR/MSFS2020_AutoFPS/releases/latest");
            lblappUrl.Visibility = Visibility.Hidden;
            if (int.TryParse(assemblyVersion.Replace(".", ""), CultureInfo.InvariantCulture, out int currentAppVersion) &&  latestAppVersionStr != null && latestAppVersionStr.Length > 70)
            { 
                latestAppVersionStr = latestAppVersionStr.Substring(latestAppVersionStr.Length - 5, 5);
                if (int.TryParse(latestAppVersionStr.Replace(".", ""), CultureInfo.InvariantCulture, out int LatestAppVersion))
                { 
                    if ((serviceModel.TestVersion && LatestAppVersion >= currentAppVersion) || LatestAppVersion > currentAppVersion)
                    {
                        lblStatusMessage.Content = "Newer app version " + (latestAppVersionStr) + " now available";
                        lblStatusMessage.Foreground = new SolidColorBrush(Colors.Green);
                    }
                    else
                    {
                        lblStatusMessage.Content = "Latest app version is installed";
                        lblStatusMessage.Foreground = new SolidColorBrush(Colors.Green);
                    }
                }   
            }
            if (serviceModel.TestVersion)
            {
                lblStatusMessage.Content = "Concept demo version";
                lblStatusMessage.Foreground = new SolidColorBrush(Colors.Green);
            }

        }
        public static string GetFinalRedirect(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            int maxRedirCount = 8;  // prevent infinite loops
            string newUrl = url;
            do
            {
                HttpWebRequest req = null;
                HttpWebResponse resp = null;
                try
                {
                    req = (HttpWebRequest)HttpWebRequest.Create(url);
                    req.Method = "HEAD";
                    req.AllowAutoRedirect = false;
                    resp = (HttpWebResponse)req.GetResponse();
                    switch (resp.StatusCode)
                    {
                        case HttpStatusCode.OK:
                            return newUrl;
                        case HttpStatusCode.Redirect:
                        case HttpStatusCode.MovedPermanently:
                        case HttpStatusCode.RedirectKeepVerb:
                        case HttpStatusCode.RedirectMethod:
                            newUrl = resp.Headers["Location"];
                            if (newUrl == null)
                                return url;

                            if (newUrl.IndexOf("://", System.StringComparison.Ordinal) == -1)
                            {
                                // Doesn't have a URL Schema, meaning it's a relative or absolute URL
                                Uri u = new Uri(new Uri(url), newUrl);
                                newUrl = u.ToString();
                            }
                            break;
                        default:
                            return newUrl;
                    }
                    url = newUrl;
                }
                catch (WebException)
                {
                    // Return the last known good URL
                    return newUrl;
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Error, "MainWindow.xaml:GetFinalRedirect", $"Exception {ex}: {ex.Message}");
                    return null;
                }
                finally
                {
                    if (resp != null)
                        resp.Close();
                }
            } while (maxRedirCount-- > 0);

            return newUrl;
        }
        protected void LoadSettings()
        {
            chkOpenWindow.IsChecked = serviceModel.OpenWindow;
            chkUseExpertOptions.IsChecked = serviceModel.UseExpertOptions;
            txtTargetFPS.Text = Convert.ToString(serviceModel.TargetFPS, CultureInfo.CurrentUICulture);
            txtFPSTolerance.Text = Convert.ToString(serviceModel.FPSTolerance, CultureInfo.CurrentUICulture);
            txtMinTLod.Text = Convert.ToString(serviceModel.MinTLOD, CultureInfo.CurrentUICulture);
            txtMaxTLod.Text = Convert.ToString(serviceModel.MaxTLOD, CultureInfo.CurrentUICulture);
            chkDecCloudQ.IsChecked = serviceModel.DecCloudQ;
            chkTLODMinGndLanding.IsChecked = serviceModel.TLODMinGndLanding;
            txtCloudRecoveryTLOD.Text = Convert.ToString(serviceModel.CloudRecoveryTLOD, CultureInfo.CurrentUICulture);
        }

        protected void UpdateStatus()
        {
            if (serviceModel.IsSimRunning)
                lblConnStatMSFS.Foreground = new SolidColorBrush(Colors.DarkGreen);
            else
                lblConnStatMSFS.Foreground = new SolidColorBrush(Colors.Red);

            if (IPCManager.SimConnect != null && IPCManager.SimConnect.IsReady)
                lblConnStatSimConnect.Foreground = new SolidColorBrush(Colors.DarkGreen);
            else
                lblConnStatSimConnect.Foreground = new SolidColorBrush(Colors.Red);

            if (serviceModel.IsSessionRunning)
                lblConnStatSession.Foreground = new SolidColorBrush(Colors.DarkGreen);
            else
                lblConnStatSession.Foreground = new SolidColorBrush(Colors.Red);
        }

        protected string CloudQualityLabel(int CloudQuality)
        {
            if (CloudQuality == 0) return "Low";
            else if (CloudQuality == 1) return "Medium";
            else if (CloudQuality == 2) return "High";
            else if (CloudQuality == 3) return "Ultra";
            else return "n/a";
        }

        protected float GetAverageFPS()
        {
            if (serviceModel.MemoryAccess != null && serviceModel.MemoryAccess.IsFgModeActive() && serviceModel.MemoryAccess.IsActiveWindowMSFS())
                return IPCManager.SimConnect.GetAverageFPS() * 2.0f;
            else
                return IPCManager.SimConnect.GetAverageFPS();
        }
        protected void UpdateLiveValues()
        {
            if (IPCManager.SimConnect != null && IPCManager.SimConnect.IsConnected)
                lblSimFPS.Content = GetAverageFPS().ToString("F0");
            else
                lblSimFPS.Content = "n/a";

            if (serviceModel.MemoryAccess != null)
            {
                lblappUrl.Visibility = Visibility.Hidden;
                lblStatusMessage.Foreground = new SolidColorBrush(Colors.Black);
                lblSimTLOD.Content = serviceModel.MemoryAccess.GetTLOD_PC().ToString("F0");
                if (serviceModel.MemoryAccess.MemoryWritesAllowed())
                {
                    lblStatusMessage.Content = serviceModel.MemoryAccess.IsDX12() ? "DX 12 | " : " DX11 | ";
                    if (serviceModel.MemoryAccess.IsVrModeActive())
                    {
                        lblSimOLOD.Content = serviceModel.MemoryAccess.GetOLOD_VR().ToString("F0");
                        lblSimCloudQs.Content = CloudQualityLabel(serviceModel.MemoryAccess.GetCloudQ_VR());
                        lblStatusMessage.Content += " VR Mode";
                    }
                    else
                    {
                        lblSimOLOD.Content = serviceModel.MemoryAccess.GetOLOD_PC().ToString("F0");
                        lblSimCloudQs.Content = CloudQualityLabel(serviceModel.MemoryAccess.GetCloudQ_PC());
                        lblStatusMessage.Content += " PC Mode";
                        lblStatusMessage.Content += (serviceModel.MemoryAccess.IsFgModeActive() ? (serviceModel.MemoryAccess.IsActiveWindowMSFS() ? " | FG Active" : " | FG Inactive") : "");
                    }
                }
                else
                {
                    lblStatusMessage.Content = "Incompatible MSFS version - Sim Values read only";
                    lblStatusMessage.Foreground = new SolidColorBrush(Colors.Red);
                }
                if (serviceModel.IsSessionRunning)
                {
                    float MinTLOD = serviceModel.MinTLOD;
                    float MaxTLOD = serviceModel.MaxTLOD;
                    if (serviceModel.UseExpertOptions)
                    {
                        MinTLOD = serviceModel.MinTLOD;
                        MaxTLOD = serviceModel.MaxTLOD;
                    }
                    else
                    {
                        if (serviceModel.MemoryAccess.IsVrModeActive())
                        {
                            MinTLOD = Math.Max(serviceModel.DefaultTLOD_VR * 0.5f, 10);
                            MaxTLOD = serviceModel.DefaultTLOD_VR * 2.0f;
                        }
                        else
                        {
                            MinTLOD = Math.Max(serviceModel.DefaultTLOD * 0.5f, 10);
                            MaxTLOD = serviceModel.DefaultTLOD * 2.0f;
                        }
                    }

                    if (serviceModel.MemoryAccess.IsFgModeActive()) lblTargetFPS.Content = "Target FG FPS";
                    else lblTargetFPS.Content = "Target FPS";
                    if (GetAverageFPS() < serviceModel.TargetFPS && serviceModel.MemoryAccess.GetTLOD_PC() == MinTLOD)
                        lblSimFPS.Foreground = new SolidColorBrush(Colors.Red);
                    else if (GetAverageFPS() > serviceModel.TargetFPS && serviceModel.MemoryAccess.GetTLOD_PC() == MaxTLOD)
                        lblSimFPS.Foreground = new SolidColorBrush(Colors.DarkGreen);
                    else lblSimFPS.Foreground = new SolidColorBrush(Colors.Black);

                    if (serviceModel.MemoryAccess.GetTLOD_PC() == MinTLOD) lblSimTLOD.Foreground = new SolidColorBrush(Colors.Red);
                    else if (serviceModel.MemoryAccess.GetTLOD_PC() == MaxTLOD) lblSimTLOD.Foreground = new SolidColorBrush(Colors.Green);
                    else if (serviceModel.tlod_step) lblSimTLOD.Foreground = new SolidColorBrush(Colors.Orange);
                    else lblSimTLOD.Foreground = new SolidColorBrush(Colors.Black);
                    if (serviceModel.DecCloudQ && serviceModel.DecCloudQActive) lblSimCloudQs.Foreground = new SolidColorBrush(Colors.Red);
                    else lblSimCloudQs.Foreground = new SolidColorBrush(Colors.Black);
                }
                else lblSimFPS.Foreground = new SolidColorBrush(Colors.Black);
            }
            else
            {
                lblSimTLOD.Content = "n/a";
                lblSimOLOD.Content = "n/a";
                lblSimCloudQs.Content = "n/a";
            }
        }

        protected void UpdateAircraftValues()
        {
            if (IPCManager.SimConnect != null && IPCManager.SimConnect.IsConnected)
            {
                var simConnect = IPCManager.SimConnect;
                lblPlaneAGL.Content = simConnect.ReadSimVar("PLANE ALT ABOVE GROUND", "feet").ToString("F0");
                lblPlaneVS.Content = (simConnect.ReadSimVar("VERTICAL SPEED", "feet per second") * 60.0f).ToString("F0");
                //if (serviceModel.OnGround)
                //    lblVSTrend.Content = "Ground";
                //else if (serviceModel.VerticalTrend > 0)
                //    lblVSTrend.Content = "Climb";
                //else if (serviceModel.VerticalTrend < 0)
                //    lblVSTrend.Content = "Descent";
                //else
                //    lblVSTrend.Content = "Cruise";
            }
            else
            {
                lblPlaneAGL.Content = "n/a";
                lblPlaneVS.Content = "n/a";
                //lblVSTrend.Content = "n/a";
            }
        }

        protected void OnTick(object sender, EventArgs e)
        {
            UpdateStatus();
            UpdateLiveValues();
            UpdateAircraftValues();
        }

        protected void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
            {
                notifyModel.CanExecuteHideWindow = false;
                notifyModel.CanExecuteShowWindow = true;
                timer.Stop();
            }
            else
            {
                LoadSettings();
                chkCloudRecoveryTLOD_WindowVisibility();
                timer.Start();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void chkUseExpertOptions_Click(object sender, RoutedEventArgs e)
        {
            serviceModel.SetSetting("useExpertOptions", chkUseExpertOptions.IsChecked.ToString().ToLower());
            LoadSettings();
            if (serviceModel.UseExpertOptions) stkpnlMSFSSettings.Visibility = Visibility.Visible;
            else stkpnlMSFSSettings.Visibility = Visibility.Collapsed;
        }

        private void chkOpenWindow_Click(object sender, RoutedEventArgs e)
        {
            serviceModel.SetSetting("openWindow", chkOpenWindow.IsChecked.ToString().ToLower());
            LoadSettings();
        }

        private void chkTLODMinGndLanding_Click(object sender, RoutedEventArgs e)
        {
            serviceModel.SetSetting("TLODMinGndLanding", chkTLODMinGndLanding.IsChecked.ToString().ToLower());
            LoadSettings();
        }
        private void chkDecCloudQ_Click(object sender, RoutedEventArgs e)
        {
            serviceModel.SetSetting("DecCloudQ", chkDecCloudQ.IsChecked.ToString().ToLower());
           LoadSettings();
            chkCloudRecoveryTLOD_WindowVisibility();

        }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox_SetSetting(sender as TextBox);
        }

        private void TextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || e.Key != Key.Return)
                return;

            TextBox_SetSetting(sender as TextBox);
        }

        private void TextBox_SetSetting(TextBox sender)
        {
            if (sender == null || string.IsNullOrWhiteSpace(sender.Text))
                return;

            string key;
            bool intValue = false;
            bool notNegative = true;
            bool zeroAllowed = false;
            switch (sender.Name)
            {
                case "txtTargetFPS":
                    key = "targetFps";
                    intValue = true;
                    break;
                case "txtFPSTolerance":
                    key = "FpsTolerance";
                    intValue = true;
                    break;
                case "txtCloudRecoveryTLOD":
                    key = "CloudRecoveryTLOD";
                    intValue = true;
                    zeroAllowed = true;
                    break;
                case "txtMinTLod":
                    key = "minTLod";
                    break;
                case "txtMaxTLod":
                    key = "maxTLod";
                    break;
                //case "txtLodStepMaxInc":
                //    key = "LodStepMaxInc";
                //    intValue = true;
                //    break;
                //case "txtLodStepMaxDec":
                //    key = "LodStepMaxDec";
                //    intValue = true;
                //    break;
                default:
                    key = "";
                    break;
            }

            if (key == "")
                return;

            if (intValue && int.TryParse(sender.Text, CultureInfo.InvariantCulture, out int iValue) && (iValue != 0 || zeroAllowed))
            {
                if (notNegative)
                    iValue = Math.Abs(iValue);
                serviceModel.SetSetting(key, Convert.ToString(iValue, CultureInfo.InvariantCulture));
            }

            if (!intValue && float.TryParse(sender.Text, new RealInvariantFormat(sender.Text), out float fValue))
            {
                if (notNegative)
                    fValue = Math.Abs(fValue);
                serviceModel.SetSetting(key, Convert.ToString(fValue, CultureInfo.InvariantCulture));
            }

            LoadSettings();
        }

        private void txtLodStepMaxInc_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void txtLodStepMaxDec_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void chkDecCloudQ_Checked(object sender, RoutedEventArgs e)
        {

        }
        private void chkTLODMinGndLanding_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                var myProcess = new Process();
                myProcess.StartInfo.UseShellExecute = true;
                myProcess.StartInfo.FileName = "https://github.com/ResetXPDR/MSFS2020_AutoFPS/releases/latest";
                myProcess.Start();
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "MainWindow.xaml:Hyperlink_RequestNavigate", $"Exception {ex}: {ex.Message}");
            }
        }
        private void chkCloudRecoveryTLOD_WindowVisibility()
        {
            if (serviceModel.DecCloudQ)
            {
                lblCloudRecoveryTLOD.Visibility = Visibility.Visible;
                txtCloudRecoveryTLOD.Visibility = Visibility.Visible;
            }
            else
            {
                lblCloudRecoveryTLOD.Visibility = Visibility.Hidden;
                txtCloudRecoveryTLOD.Visibility = Visibility.Hidden;
            }
        }
    }
}
