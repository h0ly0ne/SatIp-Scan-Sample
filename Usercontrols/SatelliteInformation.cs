﻿/*  
    Copyright (C) <2007-2019>  <Kay Diefenthal>

    SatIp is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    SatIp is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with SatIp.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Reflection;

namespace SatIp.Usercontrols
{
    public partial class SatelliteInformation : UserControl
    {
        private SatIpDevice _device;
        private bool _isScanning = false;
        private bool _stopScanning = false;
        private int _count = 0;
        private AutoResetEvent _scanThreadStopEvent = null;
        private Thread _scanThread;
        private bool _locked;
        private IPEndPoint _remoteEndPoint;
        private UdpClient _udpclient;        
        private PATParser pat;
        private PMTParser pmt;
        private SDTParser sdt;
        private NITParser nit;
        List<IniMappingContext> _mappings = new List<IniMappingContext>();

        public SatelliteInformation()
        {
            InitializeComponent();
        }

        public SatelliteInformation(SatIpDevice device)
        {
            InitializeComponent();
            _device = device;
        }

        private void SatelliteInformation_Load(object sender, EventArgs e)
        {
            if (_device != null && _device.SupportsDVBS)
            {
                #region DVBSSources

                cbxDiseqC.Items.Add("None(Single Lnb)");
                cbxDiseqC.Items.Add("22 KHz (Tone Switch)");
                cbxDiseqC.Items.Add("Diseq c 1.x (A/B/C/D");
                cbxDiseqC.SelectedIndex = 0;

                var app = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var tuningdata = app + "\\TuningData\\Satellite";
                cbxSourceA.Items.Add("- None -");
                cbxSourceA.SelectedIndex = 0;
                cbxSourceB.Items.Add("- None -");
                cbxSourceB.SelectedIndex = 0;
                cbxSourceC.Items.Add("- None -");
                cbxSourceC.SelectedIndex = 0;
                cbxSourceD.Items.Add("- None -");
                cbxSourceD.SelectedIndex = 0;
                foreach (var str2 in Directory.GetFiles(tuningdata))
                {
                    var reader = new IniReader(str2);
                    var str3 = reader.ReadString("SATTYPE", "1");
                    var str4 = reader.ReadString("SATTYPE", "2");
                    if (!cbxSourceA.Items.Contains(str4) && str4 != "")
                    {
                        cbxSourceA.Items.Add(new IniMapping(str3 + " " + str4, str2));
                        cbxSourceB.Items.Add(new IniMapping(str3 + " " + str4, str2));
                        cbxSourceC.Items.Add(new IniMapping(str3 + " " + str4, str2));
                        cbxSourceD.Items.Add(new IniMapping(str3 + " " + str4, str2));
                    }
                }

                UpdateSatelliteSettings();

                #endregion
            }
        }

        private void UpdateSatelliteSettings()
        {
            SuspendLayout();
            if (cbxDiseqC.SelectedIndex == 0)
            {
                lblSourceB.Visible = false;
                cbxSourceB.Visible = false;
            }
            else
            {
                lblSourceB.Visible = true;
                cbxSourceB.Visible = true;
            }

            if (cbxDiseqC.SelectedIndex == 0 || cbxDiseqC.SelectedIndex == 1)
            {
                lblSourceC.Visible = false;
                cbxSourceC.Visible = false;
                lblSourceD.Visible = false;
                cbxSourceD.Visible = false;
            }
            else
            {
                lblSourceC.Visible = true;
                cbxSourceC.Visible = true;
                lblSourceD.Visible = true;
                cbxSourceD.Visible = true;
            }

            ResumeLayout();
        }

        private void CbxDiseqC_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateSatelliteSettings();
        }

        private void BtnScan_Click(object sender, EventArgs e)
        {
            if (cbxDiseqC.SelectedIndex == 0)
                if (cbxSourceA.SelectedItem.ToString() != "- None -")
                    _mappings.Add(new IniMappingContext("1", (IniMapping)cbxSourceA.SelectedItem));
            if (cbxDiseqC.SelectedIndex == 1)
            {
                if (cbxSourceA.SelectedItem.ToString() != "- None -")
                    _mappings.Add(new IniMappingContext("1", (IniMapping)cbxSourceA.SelectedItem));
                if (cbxSourceB.SelectedItem.ToString() != "- None -")
                    _mappings.Add(new IniMappingContext("2", (IniMapping)cbxSourceB.SelectedItem));
            }
            else
            {
                if (cbxSourceA.SelectedItem.ToString() != "- None -")
                    _mappings.Add(new IniMappingContext("1", (IniMapping)cbxSourceA.SelectedItem));
                if (cbxSourceB.SelectedItem.ToString() != "- None -")
                    _mappings.Add(new IniMappingContext("2", (IniMapping)cbxSourceB.SelectedItem));
                if (cbxSourceC.SelectedItem.ToString() != "- None -")
                    _mappings.Add(new IniMappingContext("3", (IniMapping)cbxSourceC.SelectedItem));
                if (cbxSourceD.SelectedItem.ToString() != "- None -")
                    _mappings.Add(new IniMappingContext("4", (IniMapping)cbxSourceD.SelectedItem));
            }

            if (_mappings.Count > 0)
            {
                if (_isScanning == false)
                    StartScanThread();
                else
                    _stopScanning = true;
            }
        }

        private void StartScanThread()
        {
            if (_scanThread != null && !_scanThread.IsAlive) StopScanThread();

            if (_scanThread == null)
            {
                _scanThreadStopEvent = new AutoResetEvent(false);
                _scanThread = new Thread(DoScan)
                {
                    Name = "SAT>IP Scan",
                    IsBackground = true,
                    Priority = ThreadPriority.Highest
                };
                _scanThread.Start();
            }
        }

        private void StopScanThread()
        {
            if (_scanThread != null)
            {
                if (!_scanThread.IsAlive)
                    _scanThread.Abort();
                else
                    _scanThreadStopEvent.Set();
                _scanThread = null;
                if (_scanThreadStopEvent != null)
                {
                    _scanThreadStopEvent.Close();
                    _scanThreadStopEvent = null;
                }
            }
        }

        private void DoScan()
        {            
            _isScanning = true;
            _stopScanning = false;
            SetControlPropertyThreadSafe(btnScan, "Text", "Stop Search");
            foreach (var mapping in _mappings)
            {
                IniReader reader = new IniReader(mapping.Mapping.File);
                var Count = reader.ReadInteger("DVB", "0", 0);                
                var Index = 1;
                string source = mapping.Source;
                string tuning;
                while (Index <= Count)
                {
                    SetControlPropertyThreadSafe(lblPAT, "BackColor", Color.LimeGreen);
                    SetControlPropertyThreadSafe(lblPMT, "BackColor", Color.LimeGreen);
                    SetControlPropertyThreadSafe(lblNIT, "BackColor", Color.LimeGreen);
                    SetControlPropertyThreadSafe(lblSDT, "BackColor", Color.LimeGreen);
                    Dictionary<int, PMTParser> pmtTables = new Dictionary<int, PMTParser>();
                    if (_stopScanning) return;
                    float percent = ((float)(Index)) / Count;
                    percent *= 100f;
                    if (percent > 100f) percent = 100f;
                    SetControlPropertyThreadSafe(pgbSearchResult, "Value", (int)percent);
                    string[] strArray = reader.ReadString("DVB", Index.ToString()).Split(new char[] { ',' });
                    if (strArray[4] == "S2")
                    {
                        tuning = string.Format("src={0}&freq={1}&pol={2}&sr={3}&fec={4}&msys=dvbs2&mtype={5}&plts=on&ro=0.35&pids=0,16", source, strArray[0].ToString(), strArray[1].ToLower().ToString(), strArray[2].ToLower().ToString(), strArray[3].ToString(), strArray[5].ToLower().ToString());
                    }
                    else
                    {
                        tuning = string.Format("src={0}&freq={1}&pol={2}&sr={3}&fec={4}&msys=dvbs&mtype={5}&pids=0,16", source, strArray[0].ToString(), strArray[1].ToLower().ToString(), strArray[2].ToString(), strArray[3].ToString(), strArray[5].ToLower().ToString());
                    }
                    RtspStatusCode statuscode;
                    if (string.IsNullOrEmpty(_device.RtspSession.RtspSessionId))
                    {
                        _device.RtspSession.Setup(tuning, TransmissionMode.Unicast);
                        _device.RtspSession.Play(string.Empty);
                        _device.RtspSession.RecieptionInfoChanged += RtspSession_RecieptionInfoChanged;
                        _udpclient = new UdpClient(_device.RtspSession.RtpPort);
                        _remoteEndPoint = new IPEndPoint(IPAddress.Parse(_device.RtspSession.Destination), _device.RtspSession.RtpPort);
                        Thread.Sleep(2500);
                    }                                        
                    /* Say the Sat>IP server we want Receives the Recieption Details SDP */
                    statuscode = _device.RtspSession.Describe();
                    /* the Thread.Sleep is needed for Sat>Ip Devices (Octopus Net) that report the locked to late without this locked will the Frequency skip  */
                    Thread.Sleep(5000);
                    if (_locked)
                    {
                        /* Say the Sat>IP server we want Receives the ProgramAssociationTable */
                        //_device.RtspSession.Play("&addpids=0");
                        if (GetPAT(_udpclient, _remoteEndPoint, out pat))
                        {
                            /* Say the Sat>IP server we want not more Receives the ProgramAssociationTable */
                            _device.RtspSession.Play("&delpids=0");
                            /* Check the Length of pat.Programs for pmt should it not more 14 Pids at once inside the Play Request possible fix is to split into Chuncks*/

                            /* Loop the ProgramAssociationTable Programs */
                            //foreach (var i in pat.Programs)
                            //{
                            //    if (i.Key == 0)
                            //    {
                            //        /* Say the Sat>IP server we want Receives the NetworkinformationTable  */
                            //        _device.RtspSession.Play(string.Format("&addpids={0}", i.Value));
                            //        if (GetNIT(_udpclient, _remoteEndPoint, pat.TransportStreamId, out nit))
                            //        {
                            //            /* Say the Sat>IP server we want not more Receives the NetworkInformationTable */
                            //            _device.RtspSession.Play(string.Format("&delpids={0}", i.Value));
                            //        }
                            //    }
                            //    else
                            //    {
                            //        /* Say the Sat>IP server we want Receives the ProgramMapTable for Pid x */
                            //        _device.RtspSession.Play(string.Format("&addpids={0}", i.Value));
                            //        if (GetPMT(_udpclient, _remoteEndPoint, (short)i.Value, out pmt))
                            //        {
                            //            /* Say the Sat>IP server we want not more Receives the ProgramMapTable for Pid x */
                            //            _device.RtspSession.Play(string.Format("&delpids={0}", i.Value));
                            //        }
                            //    }
                            //}
                        }
                        /*Now we had in the best case the ProgramAssociationTable, NetworkInformationTable, the ProgramMapTables for each 
                            * in ProgramAssociationTable referenced Program and need now the ServiceDescription Table
                            * Say the Sat>IP server we want Receives the ServiceDescriptionTable */
                        _device.RtspSession.Play("&addpids=17");

                        GetSDT(_udpclient, _remoteEndPoint, out sdt);
                        /* Say the Sat>IP server we want not more Receives the ServiceDescriptionTable */
                        _device.RtspSession.Play(string.Format("&delpids={0}", 17));
                        /* 
                            * From the ServiceDescriptionTable get we the
                            * Service ID                         
                            * ServiceName
                            * ServiceType
                            * ServiceProvider
                            * If the Service is Scrambled or Not
                            */
                        /* From ProgramMapTable get we 
                            * ProgramClockReference (PCRPID)
                            * Video PID
                            * one or more Audio PIDS
                            * Teletext PID
                            * SubTitle PID
                            */
                        /* The Service Object should contain follow fields
                            * Tuning Informations see Sat>Ip Specification 
                            * all ServiceDescription Fields 
                            * all ProgramMapTable Fields                        
                            */
                        /* add something to the Listview To inform the User what is found  */

                        int[] serviceids = sdt.Services;
                        foreach (int serviceid in serviceids)
                        {
                            Channel chan = new Channel();                            
                            chan.ServiceType = sdt.GetServiceDescription(serviceid).ServiceType;
                            chan.ServiceName = sdt.GetServiceDescription(serviceid).ServiceName;
                            chan.ServiceProvider = sdt.GetServiceDescription(serviceid).ProviderName;
                            chan.ServiceId = sdt.GetServiceDescription(serviceid).ServiceID;
                            chan.Schedule = sdt.GetServiceDescription(serviceid).EitScheduleFlag;
                            chan.PresentFollow = sdt.GetServiceDescription(serviceid).EitPresentFollowingFlag;
                            chan.Status = sdt.GetServiceDescription(serviceid).RunningStatus;
                            chan.Scrambled = sdt.GetServiceDescription(serviceid).FreeCaMode;
                            AddResults(chan);
                        }
                        Thread.Sleep(500);
                    }
                    Index++;                    
                }                
            }
            _device.RtspSession.TearDown();
            SetControlPropertyThreadSafe(pgbSearchResult, "Value", 100);
            _isScanning = false;
            SetControlPropertyThreadSafe(btnScan, "Text", "Start Search");
            StopScanThread();
        }
        private void RtspSession_RecieptionInfoChanged(object sender, RtspSession.RecieptionInfoArgs e)
        {
            SetControlPropertyThreadSafe(pgbSignalLevel, "Value", e.Level);
            SetControlPropertyThreadSafe(pgbSignalQuality, "Value", e.Quality);
            _locked = e.Locked;
        }
        private static void SetControlPropertyThreadSafe(Control control, string propertyName, object propertyValue)
        {
            if (control.InvokeRequired)
                control.Invoke(new SetControlPropertyThreadSafeDelegate
                    (SetControlPropertyThreadSafe), control, propertyName, propertyValue);
            else
                control.GetType().InvokeMember(
                    propertyName,
                    BindingFlags.SetProperty,
                    null,
                    control,
                    new[] { propertyValue });
        }

        private void AddResults(Channel chan)
        {     
            if (lwResults.InvokeRequired)
            {
                lwResults.Invoke(new AddResultDelegate(AddResults), chan);
            }
            else
            {
                _count++;
                string[] items =
                {
                    chan.Frequency.ToString(),
                    chan.ServiceType.ToString(),
                    chan.ServiceName,
                    chan.ServiceProvider,
                    chan.ServiceId.ToString(),
                    chan.Schedule.ToString(),
                    chan.PresentFollow.ToString(),
                    chan.Status.ToString(),
                    chan.Scrambled.ToString()
                };
                var lstItem = new ListViewItem(items)
                {
                    Checked = true
                };
                lwResults.Items.Add(lstItem);
                //label1.Text = string.Format("Services Found; {0}", _count.ToString());
            }
        }
        private bool GetPAT(UdpClient client, IPEndPoint endpoint, out PATParser pat)
        {

            pat = new PATParser(_device.RtspSession);
            bool retval = false;
            while ((!retval))
            {
                var receivedbytes = client.Receive(ref endpoint);
                RtpPacket h = RtpPacket.Decode(receivedbytes);
                if ((receivedbytes.Length > 12) && ((receivedbytes.Length - 12) % 188) == 0)
                {
                    double num9 = (((double)(receivedbytes.Length - 12)) / 188.0) - 1.0;
                    for (double j = 0.0; j <= num9; j++)
                    {

                        byte[] destinationarray = (byte[])Array.CreateInstance(typeof(byte), 188);
                        Array.Copy(receivedbytes, (int)Math.Round((double)(12.0 + (j * 188))), destinationarray, 0, 188);
                        pat.OnTsPacket(destinationarray);
                        if (pat.IsReady)
                        {
                            retval = true;
                        }
                        

                    }
                }
            }
            SetControlPropertyThreadSafe(lblPAT, "BackColor", Color.DarkGreen);
            return retval;
        }
        private bool GetNIT(UdpClient client, IPEndPoint endpoint, int tsid, out NITParser nit)
        {
            nit = new NITParser();
            bool retval = false;
            while ((!retval) )
            {
                var receivedbytes = client.Receive(ref endpoint);
                RtpPacket h = RtpPacket.Decode(receivedbytes);
                if ((receivedbytes.Length > 12) && ((receivedbytes.Length - 12) % 188) == 0)
                {
                    double num9 = (((double)(receivedbytes.Length - 12)) / 188.0) - 1.0;
                    for (double j = 0.0; j <= num9; j++)
                    {

                        byte[] destinationarray = (byte[])Array.CreateInstance(typeof(byte), 188);
                        Array.Copy(receivedbytes, (int)Math.Round((double)(12.0 + (j * 188))), destinationarray, 0, 188);
                        nit.OnTsPacket(destinationarray);
                        if (nit.IsReady)
                        {
                            retval = true;
                        }
                    }
                }
            }
            SetControlPropertyThreadSafe(lblNIT, "BackColor", Color.DarkGreen);
            return retval;
        }
        //private bool GetPMT(UdpClient client, IPEndPoint endpoint, short pid, out PMTParser pmt)
        //{
        //    pmt = new PMTParser(pid);
        //    bool retval = false;
        //    while ((!retval))
        //    {
        //        var receivedbytes = client.Receive(ref endpoint);
        //        RtpPacket h = RtpPacket.Decode(receivedbytes);
        //        if ((receivedbytes.Length > 12) && ((receivedbytes.Length - 12) % 188) == 0)
        //        {
        //            double num9 = (((double)(receivedbytes.Length - 12)) / 188.0) - 1.0;
        //            for (double j = 0.0; j <= num9; j++)
        //            {
        //                byte[] destinationarray = (byte[])Array.CreateInstance(typeof(byte), 188);
        //                Array.Copy(receivedbytes, (int)Math.Round((double)(12.0 + (j * 188))), destinationarray, 0, 188);

        //                pmt.OnTsPacket(destinationarray);
        //                if (pmt.IsReady)
        //                {
        //                    retval = true;
        //                }
        //            }
        //        }
        //    }
        //    SetControlPropertyThreadSafe(lblPMT, "BackColor", Color.DarkGreen);
        //    return retval;
        //}
        private bool GetSDT(UdpClient client, IPEndPoint endpoint, out SDTParser sdt)
        {
            sdt = new SDTParser();
            bool retval = false;
            while ((!retval))
            {                
                var receivedbytes = client.Receive(ref endpoint);
                RtpPacket h = RtpPacket.Decode(receivedbytes);

                if ((receivedbytes.Length > 12) && ((receivedbytes.Length - 12) % 188) == 0)
                {
                    double num9 = (((double)(receivedbytes.Length - 12)) / 188.0) - 1.0;
                    for (double j = 0.0; j <= num9; j++)
                    {
                        byte[] destinationarray = (byte[])Array.CreateInstance(typeof(byte), 188);
                        Array.Copy(receivedbytes, (int)Math.Round((double)(12.0 + (j * 188))), destinationarray, 0, 188);

                        sdt.OnTsPacket(destinationarray);
                        if (sdt.IsReady)
                        {
                            retval = true;
                        }

                    }
                }
            }
            SetControlPropertyThreadSafe(lblSDT, "BackColor", Color.DarkGreen);
            return retval;
        }
        private string GetMessageFromStatuscode(RtspStatusCode code)
        {
            var retval = string.Empty;
            switch (code)
            {
                case RtspStatusCode.BadRequest:
                    retval = "The request could not be understood by the server due to a malformed syntax. Returned when missing a character, inconsistent request (duplicate attributes), etc.";
                    break;
                case RtspStatusCode.Forbidden:
                    retval = "The server understood the request, but is refusing to fulfil it. Returned when passing an attribute value not understood by the server in a query, or an out-of-range value.";
                    break;
                case RtspStatusCode.NotFound:
                    retval = "The server has not found anything matching the Request-URI. Returned when requesting a stream with a streamID that does not exist.";
                    break;
                case RtspStatusCode.MethodNotAllowed:
                    retval = "The method specified in the request is not allowed for the resource identified by the Request-URI. Returned when applying a SETUP, PLAY or TEARDOWN method on an RTSP URI identifying the server.";
                    break;
                case RtspStatusCode.NotAcceptable:
                    retval = "The resource identified by the request is only capable of generating response message bodies which have content characteristics not acceptable according to the accept headers sent in the request. Issuing a DESCRIBE request with an accept header different from application/sdp.";
                    break;
                case RtspStatusCode.RequestTimeOut:
                    retval = "The client did not produce a request within the time that the server was prepared to wait. The client may repeat the request without modifications at any later time. E.g. issuing a PLAY request after the communication link had been idle for a period of time. The time interval has exceeded the value specified by the timeout parameter in the Session: header field of a SETUP response.";
                    break;
                case RtspStatusCode.RequestUriTooLarge:
                    retval = "The server is refusing to service the request because the Request-URI is longer than the server is willing to interpret. The RTSP protocol does not place any limit on the length of a URI. Servers should be able to handle URIs of unbounded length.";
                    break;
                case RtspStatusCode.NotEnoughBandwidth:
                    retval = "The request was refused because there is insufficient bandwidth on the in-home LAN. Returned when clients are requesting more streams than the network can carry.";
                    break;
                case RtspStatusCode.SessionNotFound:
                    retval = "The RTSP session identifier value in the Session: header field of the request is missing, invalid, or has timed out. Returned when issuing the wrong session identifier value in a request.";
                    break;
                case RtspStatusCode.MethodNotValidInThisState:
                    retval = "The client or server cannot process this request in its current state. Returned e.g. when trying to change transport parameters while the server is in the play state (e.g. change of port values, etc.).";
                    break;
                case RtspStatusCode.UnsupportedTransport:
                    retval = "The Transport: header field of the request did not contain a supported transport specification. Returned e.g. when issuing a profile that is different from RTP/AVP.";
                    break;
                case RtspStatusCode.InternalServerError:
                    retval = "The server encountered an error condition preventing it to fulfil the request. Returned when the server is not functioning correctly due to a hardware failure or a software bug or anything else that can go wrong.";
                    break;
                case RtspStatusCode.ServiceUnavailable:
                    retval = "The server is currently unable to handle the request due to a temporary overloading or maintenance of the server. Returned when reaching the maximum number of hardware and software resources, the maximum number of sessions.";
                    break;
                case RtspStatusCode.RtspVersionNotSupported:
                    retval = "The server does not support the RTSP protocol version that was used in the request message.";
                    break;
                case RtspStatusCode.OptionNotSupported:
                    retval = "A feature-tag given in the Require: header field of the request was not supported. Issuing a request with a Require: header field.";
                    break;
            }
            return retval;
        }
    }
}

