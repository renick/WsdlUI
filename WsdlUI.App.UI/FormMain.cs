/*
    Copyright 2013 Fred Bowker
    This file is part of WsdlUI.
    WsdlUI is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
    WsdlUI is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
    You should have received a copy of the GNU General Public License along with Foobar. If not, see http://www.gnu.org/licenses/.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using process = WsdlUI.App.Process;
using websvcasync = WsdlUI.App.Process.WebSvcAsync;

namespace WsdlUI.App.UI {
    public partial class FormMain : Form {

        public event EventHandler<websvcasync.EventParams.ExceptionAsyncArgs> OnException;

        bool exceptionOccurred;
        //a list of all added and currently being added wsdls is kept, this is needed as a wsdl is only added to the wsdl container after it has been downloaded.
        List<string> _addedWsdls = new List<string>();

        static readonly object _retrievelocker = new object();

        public FormMain() {
            InitializeComponent();

            uc_treeView1.WebMethodClicked += new Action<string, string>(uc_treeView1_WebMethodClicked);
            uc_treeView1.WebServiceClicked += new Action<string>(uc_treeView1_WebServiceClicked);
            uc_treeView1.AddClicked += new Action(uc_treeView1_AddClicked);
            uc_treeView1.RemoveClicked += new Action<string>(uc_treeView1_RemoveClicked);
        }

        //xml is written to a temp directory for displaying in the web browser control
        void ClearTempDirectory() {
            try {
                foreach (var filename in Directory.GetFiles(Consts.TempDirectory, "*.xml")) {
                    File.Delete(filename);
                }
            } //if running multiple instances of the application the file may already being read. Then HIDE exception.
            catch (IOException) {
            }
        }

        void uc_treeView1_RemoveClicked(string key) {

            _addedWsdls.Remove(key);

            UserControls.uc_SourceBrowser control = State.Instance.ContainerWebSvc.RemoveWebService(key);
            if (control == null) {
                uc_panelInfo1.Clear();
            }
            else {
                uc_panelInfo1.DispayControl(control);
            }
        }

        //The async add call can not be cancelled
        void uc_treeView1_AddClicked() {

            Dialogs.dg_AddWS _dialog = new Dialogs.dg_AddWS();
            _dialog.PopulateForm();
            DialogResult result = _dialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK) {
                if (_dialog.ErrorMessage != null) {
                    uc_log1.LogErrorMessage(_dialog.ErrorMessage);
                    return;
                }

                if (State.Instance.ContainerWebSvc.ContainsWebService(_dialog.SelectedWSDL)) {
                    uc_log1.LogErrorMessage("the project already contains the selected wsdl, to refresh remove the node and add it again");
                    return;
                }

                uc_log1.LogInfoMessage("start adding wsdl - " + _dialog.SelectedWSDL);

                CallWebSvcRetrieveAsyc(_dialog.SelectedWSDL, false);
            }

        }


        void CallWebSvcRetrieveAsyc(string wsdl, bool onStartup) {

            //validation of url format etx is done on user input, the file exists check is performed here as it is to slow to perform on user input.
            if (!CheckFileExists(wsdl)) {
                uc_log1.LogErrorMessage(wsdl + " file location does not exist");
                return;
            }

            if (_addedWsdls.Contains(wsdl)) {
                //if the user tries to add the wsdl again while the preivious add is in process this exception is thrown
                uc_log1.LogErrorMessage(wsdl + " is currently being added please wait for it to complete");
                return;
            }

            _addedWsdls.Add(wsdl);

            Thread thread = new Thread(() => {

                var call = new websvcasync.Operations.RetrieveAsyncOp(wsdl, State.Instance.ConfigProxy, AppConfig.Instance.WebSvcRetrieveTimeout);
                call.OnComplete += asyncCall_OnRerieveComplete;
                call.OnTimeout += asyncCall_OnRetrieveTimeout;
                call.OnException += asyncCall_OnRerieveException;
                call.OnWebException += asyncCall_OnRerieveWebException;

                if (onStartup) {
                    call.OnException += asyncCall_OnRerieveExceptionStartup;
                }
                else {
                    call.OnException += asyncCall_OnRerieveException;
                }

                call.Start();

            });

            //the windows forms control must be updated by a thread with single threaded appartment property
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

        }

        bool CheckFileExists(string wsdl) {
            Uri outUri = null;
            Uri.TryCreate(wsdl, UriKind.Absolute, out outUri);

            if (outUri.IsFile) {
                return (File.Exists(outUri.AbsolutePath));
            }

            return true;
        }

        void asyncCall_OnRerieveWebException(object sender, websvcasync.EventParams.ExceptionAsyncArgs e) {

            process.Logger.Instance.Log.Info("Start " + e.Name);

            _addedWsdls.Remove(e.Name);

            Invoke((MethodInvoker)(() => {

                uc_log1.LogErrorMessage(e.Message);
                uc_log1.LogInfoMessage("finish adding wsdl - " + e.Name);

            }));

        }

        void asyncCall_OnRetrieveTimeout(object sender, websvcasync.EventParams.TimeoutAsyncArgs e) {

            //the program could keep a list of thread calls and on exception cleanly cancel all of the calls, this would cancel the timeouts
            //as in the event of an exception the only action that can be performed is to close the application we will simply not display the timeout message
            if (exceptionOccurred)
                return;

            Invoke((MethodInvoker)(() => {

                uc_log1.LogErrorMessage("timeout adding wsdl - " + e.Name);
                uc_log1.LogInfoMessage("finish adding wsdl - " + e.Name);

            }));
        }

        public void asyncCall_OnRerieveException(object sender, websvcasync.EventParams.ExceptionAsyncArgs e) {

            process.Logger.Instance.Log.Info("Start " + e.Name);

            exceptionOccurred = true;

            if (OnException != null) {
                OnException(sender, e);
            }

        }

        public void asyncCall_OnRerieveExceptionStartup(object sender, websvcasync.EventParams.ExceptionAsyncArgs e) {

            exceptionOccurred = true;

            //if a wsdl being loaded from the startup file results in an exception the program closes. This means that we will not be able to start the program to remove the faulty wsdl.
            //as a workaround set the starup wsdls file to disabled and save.
            try {
                State.Instance.ConfigStartupWsdls.Enabled = false;
                State.Instance.Save();
            }
            catch (Exception) {
            }

            if (OnException != null) {
                OnException(sender, e);
            }

        }

        public void asyncCall_OnRerieveComplete(object sender, websvcasync.EventParams.RetrieveCompleteAsyncArgs e) {

            lock (_retrievelocker) {

                if (exceptionOccurred)
                    return;

                //include sleep to make the building of the tree smoother
                Thread.Sleep(250);

                Invoke((MethodInvoker)(() => {

                    State.Instance.ContainerWebSvc.Populate(e.Result);

                    uc_treeView1.AddClickedComplete(e.Result);
                    uc_log1.LogInfoMessage("finish adding wsdl - " + e.Name);

                }));
            }

        }

        void uc_treeView1_WebServiceClicked(string key) {
            UserControls.uc_SourceBrowser control = State.Instance.ContainerWebSvc.GetWebService(key);
            uc_panelInfo1.DispayControl(control);
        }

        void uc_treeView1_WebMethodClicked(string wsKey, string wmKey) {
            UserControls.uc_Wm control = State.Instance.ContainerWebSvc.GetWebMethod(wsKey, wmKey);
            uc_panelInfo1.DispayControl(control);
        }

        public void Setup() {
            State.Instance.Load();

            if (State.Instance.ConfigUpdate.Enabled) {
                CheckForUpdate();
            }
            if (State.Instance.ConfigStartupWsdls.Enabled) {
                LoadWsdls();
            }
        }

        void LoadWsdls() {
            foreach (var v in State.Instance.ConfigStartupWsdls.Wsdls) {
                CallWebSvcRetrieveAsyc(v, true);
            }
        }

        void CheckForUpdate() {

            var updateCheck = new websvcasync.Operations.UpdateAsyncOp(AppConfig.Instance.UpdateUrl, AppInfo.Instance.Version, State.Instance.ConfigProxy, AppConfig.Instance.UpdateTimeout);
            updateCheck.OnComplete += updateCheck_OnComplete;

            Thread thread = new Thread(() =>
               updateCheck.Start()
               );
            thread.Start();
        }

        void updateCheck_OnComplete(object sender, websvcasync.EventParams.UpdateAsyncArgs e) {
            Invoke((MethodInvoker)(() => {

                if (e.UpdateAvailable) {
                    string resultMsg = string.Format(Consts.UpdateFormatMsg, e.Version, e.DownloadUrl);
                    uc_log1.LogSystemMessage(resultMsg);
                }

            }));
        }

        public void CleanUp() {
            State.Instance.Save();
            ClearTempDirectory();
        }

        void exitToolStripMenuItem_Click(object sender, EventArgs e) {
            Close();
        }

        void configProxyToolStripMenuItem_Click(object sender, EventArgs e) {
            WsdlUI.App.UI.Dialogs.dg_ConfigProxy dialog = new Dialogs.dg_ConfigProxy();
            dialog.ShowDialog();
        }

        void helpAboutToolStripMenuItem_Click(object sender, EventArgs e) {
            WsdlUI.App.UI.Dialogs.dg_HelpAbout dialog = new Dialogs.dg_HelpAbout();
            dialog.ShowDialog();
        }

        void configUpdateToolStripMenuItem_Click(object sender, EventArgs e) {
            WsdlUI.App.UI.Dialogs.dg_ConfigUpdate dialog = new Dialogs.dg_ConfigUpdate();

            dialog.ShowDialog();
            dialog.RetrieveForm();
        }

        void helpUpdateToolStripMenuItem_Click(object sender, EventArgs e) {
            WsdlUI.App.UI.Dialogs.dg_HelpUpdate dialog = new Dialogs.dg_HelpUpdate();
            dialog.ShowDialog();
        }

        void configStartupWsdlsToolStripMenuItem_Click(object sender, EventArgs e) {
            WsdlUI.App.UI.Dialogs.dg_ConfigStartupWsdls dialog = new Dialogs.dg_ConfigStartupWsdls();
            dialog.ShowDialog();
        }

        private void FormMain_Load(object sender, EventArgs e) {
            menuStrip1.Font = DefaultFonts.Instance.Medium;
        }


    }
}










