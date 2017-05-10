using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using HidLibrary;
using XmlRpc;

namespace TeslaQR
{
    public partial class Form1 : Form
    {
        private const string PRESSFIT = "P1104383";
        private const string GEAR = "P1104385";
        private const string PINION = "P1104384";
        private static readonly  string[] QUALIFIED_SCANNER_ID = { "0x0907", "0x0927" };
        private static string odooUrl = "http://192.168.10.143:8069/xmlrpc/2", db = "odoo-batom", pass = "batom", user = "admin";
        private HidDevice _hidDevice = null;
        public delegate void ReadHandlerDelegate(HidReport report);
        
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            GetHidDevice();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            Save();
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            ClearFields();
        }

        private void ReadProcess(HidReport report)
        {
            try
            {
                BeginInvoke(new ReadHandlerDelegate(ReadHandler), new object[] { report });
            }
            catch { }
        }

        private void ReadHandler(HidReport report)
        {
            int len = Convert.ToInt32(report.Data[0]);
            string barcode = "";
            for (int i = 4; i < 4 + len; i++)
            {
                barcode += Convert.ToChar(report.Data[i]);
            }
            
            if (barcode == "Save")
                Save();
            else
            {
                var prefix = barcode.Substring(0, 8);
                if (prefix == PRESSFIT)
                    txtPressfit.Text = barcode;
                else if (prefix == GEAR)
                    txtGear.Text = barcode;
                else if (prefix == PINION)
                    txtPinion.Text = barcode;
            }

            _hidDevice.ReadReport(ReadProcess);
        }

        private static void WriteResult(bool success)
        {
            MessageBox.Show("Write result: " + success);
        }

        private void GetHidDevice()
        {
            HidDevice[] deviceList = HidDevices.Enumerate(0x0c2e).ToArray();
            if (deviceList.Length <=0)
            {
                MessageBox.Show("找不到 Honeywell 掃描槍", "錯誤");
                Application.Exit();
            }
            else
            {
                string deviceDescription = "";
                foreach (HidDevice hidDevice in deviceList)
                {
                    deviceDescription += "\n" + hidDevice.Description + " (" + hidDevice.Attributes.ProductHexId + ")";
                    if (QUALIFIED_SCANNER_ID.Contains(hidDevice.Attributes.ProductHexId))
                    {
                        _hidDevice = hidDevice;
                        _hidDevice.OpenDevice();
                        _hidDevice.MonitorDeviceEvents = true;
                        _hidDevice.ReadReport(ReadProcess);
                        break;
                    }
                }
                if (_hidDevice == null)
                {
                    picHidBarcode.Visible = true;
                    btnSave.Visible = false;
                    MessageBox.Show("請掃描螢幕下方條碼將掃描槍設定為 HID 模式後重新啟動 TeslaQR" + deviceDescription, "錯誤");
                }
            }
        }

        private void Save() {
            string pressfit_qr = "";
            string gear_qr = "";
            string pinion_qr = "";
            if (txtPressfit.Text != null)
                pressfit_qr = txtPressfit.Text.Trim();
            if (txtGear.Text != null)
                gear_qr = txtGear.Text.Trim();
            if (txtPinion.Text != null)
                pinion_qr = txtPinion.Text.Trim();
            if (pressfit_qr == "" && gear_qr == "" && pinion_qr == "")
            {
                MessageBox.Show("請掃描 QR Code", "錯誤");
            }
            else
            {
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    XmlRpcClient client = new XmlRpcClient();
                    client.Url = odooUrl;
                    client.Path = "common";           

                    // LOGIN

                    XmlRpcRequest requestLogin = new XmlRpcRequest("authenticate");
                    requestLogin.AddParams(db, user, pass, XmlRpcParameter.EmptyStruct());

                    XmlRpcResponse responseLogin = client.Execute(requestLogin);

                    if (responseLogin.IsFault())
                    {
                        Cursor.Current = Cursors.Default;
                        MessageBox.Show("無法連線到資料庫，請通知 IT 人員", "錯誤");
                    }
                    else
                    {
                        client.Path = "object";

                        List<object> domain = new List<object>();
                        if (pressfit_qr != "")
                            domain.Add(XmlRpcParameter.AsArray("pressfit_qr", "=", pressfit_qr));
                        if (gear_qr != "")
                            domain.Add(XmlRpcParameter.AsArray("gear_qr", "=", gear_qr));
                        if (pinion_qr != "")
                            domain.Add(XmlRpcParameter.AsArray("pinion_qr", "=", pinion_qr));
                        
                        if (domain.Count >= 2)
                        {
                            if (domain.Count == 3)
                                domain.Insert(0, "|");
                            domain.Insert(0, "|");
                        }
                        XmlRpcRequest requestSearch = new XmlRpcRequest("execute_kw");
                        requestSearch.AddParams(db, responseLogin.GetInt(), pass, "batom.tesla.qrcode", "search_read", 
                            XmlRpcParameter.AsArray(
                                domain
                            ),
                            XmlRpcParameter.AsStruct(
                                XmlRpcParameter.AsMember("fields", XmlRpcParameter.AsArray("pressfit_qr", "gear_qr", "pinion_qr"))
                                // ,XmlRpcParameter.AsMember("limit", 2)
                            )
                        );                      

                        XmlRpcResponse responseSearch = client.Execute(requestSearch);

                        if (responseSearch.IsFault())
                        {
                            MessageBox.Show(responseSearch.GetFaultString(), "錯誤");
                        }
                        else if (!responseSearch.IsArray())
                        {
                            Cursor.Current = Cursors.Default;
                            MessageBox.Show("查詢資料庫異常，請通知 IT 人員", "錯誤");
                        }
                        else
                        {
                            List<Object> valueList = responseSearch.GetArray();
                            if (valueList.Count == 0)
                            {
                                Dictionary<string, object> values = new Dictionary<string, object>();
                                if (pressfit_qr != "")
                                    values["pressfit_qr"] = pressfit_qr;
                                if (gear_qr != "")
                                    values["gear_qr"] = gear_qr;
                                if (pinion_qr != "")
                                    values["pinion_qr"] = pinion_qr;
                                XmlRpcRequest requestCreate = new XmlRpcRequest("execute_kw");
                                requestCreate.AddParams(db, responseLogin.GetInt(), pass, "batom.tesla.qrcode", "create", 
                                    XmlRpcParameter.AsArray(values)
                                );                      

                                XmlRpcResponse responseCreate = client.Execute(requestCreate);
                                if (responseCreate.IsFault())
                                {
                                    MessageBox.Show(responseCreate.GetFaultString(), "錯誤");
                                }
                                else
                                {
                                    AutoClosingMessageBox.Show("已儲存", "完成", 1000);
                                    ClearFields();
                                }
                            }
                            else
                            {
                                string db_pressfit_qr = "";
                                string db_gear_qr = "";
                                string db_pinion_qr = "";
                                int id = -1;
                                foreach (Dictionary<string, object> valueDictionary in valueList)
                                {
                                    foreach (KeyValuePair<string, object> kvp in valueDictionary)
                                    {
                                        if (kvp.Key == "id")
                                            id = (int)kvp.Value;
                                        else if (kvp.Key == "pressfit_qr" && kvp.Value is string)
                                            db_pressfit_qr = (string)kvp.Value;
                                        else if (kvp.Key == "gear_qr" && kvp.Value is string)
                                            db_gear_qr = (string)kvp.Value;
                                        else if (kvp.Key == "pinion_qr" && kvp.Value is string)
                                            db_pinion_qr = (string)kvp.Value;
                                    }
                                }
                                
                                if ((pressfit_qr == "" || pressfit_qr == db_pressfit_qr) &&
                                    (gear_qr == "" || gear_qr == db_gear_qr) && 
                                    (pinion_qr == "" || pinion_qr == db_pinion_qr))
                                {
                                    Cursor.Current = Cursors.Default;
                                    MessageBox.Show("QR code 組合已存在", "錯誤");
                                }
                                else if (ValueConflict(pressfit_qr, db_pressfit_qr) ||
                                    ValueConflict(gear_qr, db_gear_qr) ||
                                    ValueConflict(pinion_qr, db_pinion_qr))
                                {
                                    Cursor.Current = Cursors.Default;
                                    MessageBox.Show("與資料庫中下列 QR code 組合衝突，無法儲存：\n\n" +
                                        "總成：" + db_pressfit_qr + "\n" +
                                        "軸：　" + db_pinion_qr + "\n" +
                                        "餅：　" + db_gear_qr,
                                        "錯誤"
                                    );
                                }
                                else
                                {
                                    Dictionary<string, object> values = new Dictionary<string, object>();
                                    if (pressfit_qr != "")
                                        values["pressfit_qr"] = pressfit_qr;
                                    if (gear_qr != "")
                                        values["gear_qr"] = gear_qr;
                                    if (pinion_qr != "")
                                        values["pinion_qr"] = pinion_qr;
                                    XmlRpcRequest requestWrite = new XmlRpcRequest("execute_kw");
                                    requestWrite.AddParams(db, responseLogin.GetInt(), pass, "batom.tesla.qrcode", "write",
                                        XmlRpcParameter.AsArray(XmlRpcParameter.AsArray(id), values)
                                    );                      

                                    XmlRpcResponse responseWrite = client.Execute(requestWrite);

                                    if (responseWrite.IsFault())
                                    {
                                        MessageBox.Show(responseWrite.GetFaultString(), "錯誤");
                                    }
                                    else
                                    {
                                        AutoClosingMessageBox.Show("已儲存", "完成", 1000);
                                        ClearFields();
                                    }
                                }
                            }
                        }
                        Cursor.Current = Cursors.Default;
                    }
                }
                catch
                {
                    MessageBox.Show("無法儲存，請通知IT人員", "錯誤");
                }
            }
        }
        
        private bool ValueConflict(string value, string dbValue)
        {
            return (value != dbValue && value != "" && dbValue != "");
        }
        
        private void ClearFields()
        {
            txtPressfit.Clear();
            txtGear.Clear();
            txtPinion.Clear();
        }

        private class AutoClosingMessageBox
        {
            System.Threading.Timer _timeoutTimer;
            string _caption;
            AutoClosingMessageBox(string text, string caption, int timeout)
            {
                _caption = caption;
                _timeoutTimer = new System.Threading.Timer(OnTimerElapsed,
                    null, timeout, System.Threading.Timeout.Infinite);
                using (_timeoutTimer)
                    MessageBox.Show(text, caption);
            }
            public static void Show(string text, string caption, int timeout)
            {
                new AutoClosingMessageBox(text, caption, timeout);
            }
            void OnTimerElapsed(object state)
            {
                IntPtr mbWnd = FindWindow("#32770", _caption); // lpClassName is #32770 for MessageBox
                if (mbWnd != IntPtr.Zero)
                    SendMessage(mbWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                _timeoutTimer.Dispose();
            }
            const int WM_CLOSE = 0x0010;
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
            static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);
        }
    }
}
