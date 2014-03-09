﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using System.Windows;

namespace SerialPort
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            disconnectButton.Enabled = false;
            startBurnButton.Enabled = false;
            startTestButton.Enabled = false;
            sendButton.Enabled = false;

            foreach (String s in System.IO.Ports.SerialPort.GetPortNames()) 
            {
                DUTPort.Items.Add(s);
                REFPort.Items.Add(s);
            }

            // add selection items for buad rate
            cmbbaudrate.Items.Add("115200");
            cmbbaudrate.Items.Add("3000000");

            // set default Serial Port configuration
            cmbbaudrate.Text = "115200";
        }

        // sport will be null when declared
        public System.IO.Ports.SerialPort dutPort;
        public System.IO.Ports.SerialPort refPort;

        // 0: didn't receive, 1: received with success, 2: received with error
        Int16 isDUTReceivedTxTestEvt = 0;
        Int16 isDUTReceivedRxTestEvt = 0;
        Int16 isDUTReceivedTestEndEvt = 0;

        Int16 isREFReceivedTxTestEvt = 0;
        Int16 isREFReceivedRxTestEvt = 0;
        Int16 isREFReceivedTestEndEvt = 0;

        Int32 DUTreceivedPacketsNum = 0;
        Int32 REFreceivedPacketsNum = 0;

        // called by event handler to check the sender is dutPort or refPort, and return "DUT" or "REF".
        public String getPortNameFromSenderOrPortObject(object sender)
        {
            String portname = null;

            if (sender == dutPort)
            {
                portname = "DUT";
            }
            else if (sender == refPort)
            {
                portname = "REF";
            }

            return portname;
        }

        // called by event handler to check the sender is dutPort or refPort, and return dutPort or refPort.
        public System.IO.Ports.SerialPort getPortObjectFromSender(object sender)
        {
            System.IO.Ports.SerialPort sport = null;

            if (sender == dutPort)
            {
                sport = dutPort;
            }
            else if (sender == refPort)
            {
                sport = refPort;
            }

            return sport;
        }

        public void serialport_connect(String com, int baudrate , Parity parity, int databits, StopBits stopbits, ref System.IO.Ports.SerialPort sportobject) 
        {
            DateTime dt = DateTime.Now;
            String dtn = dt.ToShortTimeString();
            String portname = getPortNameFromSenderOrPortObject(sportobject);

            sportobject = new System.IO.Ports.SerialPort(
            com, baudrate, parity, databits, stopbits);

            try
            {
                sportobject.Open();
                disconnectButton.Enabled = true;
                connectButton.Enabled = false;
                startBurnButton.Enabled = true;
                startTestButton.Enabled = true;
                sendButton.Enabled = true;
                txtReceive.AppendText("[" + dtn + "] " + portname + " is Connected\n");
                txtReceive.AppendText("\n");

                // add event hander to sportobject. this will change the content of the object, that's why we use "ref" keyword.
                sportobject.DataReceived += new SerialDataReceivedEventHandler(sport_DataReceived);
            }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "Error"); }
        }

        // Connect Button. Will connect both DUT and REF.
        private void connectButton_Click(object sender, EventArgs e)
        {
            String DUT_com = DUTPort.Text;
            String REF_com = REFPort.Text;

            // if port is not selected
            if (DUT_com == "" || REF_com == "")
                return;

            int baudrate = Convert.ToInt32(cmbbaudrate.Text);
            Parity parity = (Parity)Enum.Parse(typeof(Parity), "None");
            int databits = 8;
            StopBits stopbits = (StopBits)Enum.Parse(typeof(StopBits), "One");

            // connect DUT
            serialport_connect(DUT_com, baudrate, parity, databits, stopbits, ref dutPort);
            // connect REF
            serialport_connect(REF_com, baudrate, parity, databits, stopbits, ref refPort);
        }

        private delegate void SetTextCallback(string text);

        private void showReceivedData(string text)
        {
            this.txtReceive.AppendText(text + Environment.NewLine + Environment.NewLine);
        }

        // Check if we received targeted HCI events, and set flags to inform StartButton thread, so that we know the test result.
        private void checkReceivedEventAndSetFlags(object receivedPort, byte[] eventBytes)
        {
            if (eventBytes.Length < 7 || eventBytes[0] != 0x04 || eventBytes[1] != 0x0e || eventBytes[3] != 0x00)
            {
                // not a valid hci event packet. do nothing for now.
                return;
            }

            byte length = eventBytes[2];
            byte status = eventBytes[6];

            // 0: didn't receive, 1: received with success, 2: received with error
            Int16 flagValue = 1;

            // check for HCI_LE_Transmitter_Test: 0x201e.
            if (eventBytes[4] == 0x1e && eventBytes[5] == 0x20)
            {
                if (length != 0x04 || status != 0x00 || eventBytes.Length != 7)
                    flagValue = 2;

                if (receivedPort == dutPort)
                    isDUTReceivedTxTestEvt = flagValue;
                else if (receivedPort == refPort)
                    isREFReceivedTxTestEvt = flagValue;
            }
            // check for HCI_LE_Receiver_Test: 0x201d
            else if (eventBytes[4] == 0x1d && eventBytes[5] == 0x20)
            {
                if (length != 0x04 || status != 0x00 || eventBytes.Length != 7)
                    flagValue = 2;

                if (receivedPort == dutPort)
                    isDUTReceivedRxTestEvt = flagValue;
                else if (receivedPort == refPort)
                    isREFReceivedRxTestEvt = flagValue;
            }
            // check for HCI_Test_End: 0x201f
            else if (eventBytes[4] == 0x1f && eventBytes[5] == 0x20)
            {
                if (length != 0x06 || status != 0x00 || eventBytes.Length != 9)
                    flagValue = 2;

                if (receivedPort == dutPort)
                {
                    isDUTReceivedTestEndEvt = flagValue;
                    DUTreceivedPacketsNum = (Int32)eventBytes[7] + ((Int32)eventBytes[8] << 8);
                }
                else if (receivedPort == refPort)
                {
                    isREFReceivedTestEndEvt = flagValue;
                    REFreceivedPacketsNum = (Int32)eventBytes[7] + ((Int32)eventBytes[8] << 8);
                }
            }
        }

        // Event handler that will be called when Serial Port receving data. Will check sender to determine if DUT or REF send this event.
        private void sport_DataReceived(object sender, SerialDataReceivedEventArgs e) 
        {
            DateTime dt = DateTime.Now;
            String dtn = dt.ToShortTimeString();

            // determine the sender is DUT or REF 
            String portname = getPortNameFromSenderOrPortObject(sender);
            System.IO.Ports.SerialPort sport = getPortObjectFromSender(sender);

            // use List class to store variable length bytes data.
            List<byte> bytesReceived = new List<byte>();
            int dataLen = sport.BytesToRead;

            while (dataLen-- > 0) 
            {
                bytesReceived.Add(Convert.ToByte(sport.ReadByte()));
            }
            byte[] bytesReceivedArray = bytesReceived.ToArray();

            // check if we received targeted HCI events, and set flags to inform StartButton thread, so that we know the test result.
            checkReceivedEventAndSetFlags(sender, bytesReceivedArray);

            // convert byte list to byte array, then to string.
            String ReceivedHexStr = BitConverter.ToString(bytesReceivedArray);
            // add "[time] Received" to string
            ReceivedHexStr = "[" + dtn + "] " + portname + " Received" + Environment.NewLine + ReceivedHexStr;

            // invoke UI thread to show received data on txtReceive. Use delegate.
            txtReceive.BeginInvoke(new SetTextCallback(showReceivedData), ReceivedHexStr);
        }

        // Called by Send Button Event Handler. Will send hci cmd to both DUT and REF.
        private void sendHexArray(byte[] bytesToSend, int len, String hciCmdName, System.IO.Ports.SerialPort sport)
        {
            if (sport != null && sport.IsOpen == true)
            {
                DateTime dt = DateTime.Now;
                String dtn = dt.ToShortTimeString();
                String portname = getPortNameFromSenderOrPortObject(sport);

                // send hex array. ex 0x01, 0x31, 0xf3, 0x01, 0x16
                sport.Write(bytesToSend, 0, len);

                // convert hex array to string. ex. convert hex bytes array {0x01, 0x31, 0xf3, 0x01, 0x16} to string 01-31-fe-01-16
                String bytesToSendHexString = BitConverter.ToString(bytesToSend);

                // show hex format string to log window.
                String ReceivedHexStr = "[" + dtn + "] " + "Sent Command   <<< " + hciCmdName + " >>>  to " + portname +
                    Environment.NewLine + bytesToSendHexString;
                txtReceive.BeginInvoke(new SetTextCallback(showReceivedData), ReceivedHexStr);
            }
        }

        // Send Button. Send msg to both REF and DUT.
        private void sendButton_Click(object sender, EventArgs e)
        {
            // make sure sport is already assigned a value by serialport_connect method.
            if (refPort == null || dutPort == null || txtDatatoSend.Text.Trim() == "")
                return;

            // get text on send box, and then split input text into byte array. Trim() will remove start and end space.
            String inputText = txtDatatoSend.Text.Trim();
            byte[] bytesToSend = inputText.Split(' ').Select(s => Convert.ToByte(s, 16)).ToArray();

            // call sendHexArray() to send out
            sendHexArray(bytesToSend, bytesToSend.Length, "Test Command", dutPort);
            sendHexArray(bytesToSend, bytesToSend.Length, "Test Command", refPort);
        }

        // Disconnect Button. Disconnect both DUT and REF.
        private void disconnectButton_Click(object sender, EventArgs e)
        {
            DateTime dt = DateTime.Now;
            String dtn = dt.ToShortTimeString();

            if (dutPort.IsOpen) 
            {
                dutPort.Close();
                txtReceive.AppendText("[" + dtn + "] " + "DUT is Disconnected\n");
                txtReceive.AppendText("\n");
            }

            if (refPort.IsOpen)
            {
                refPort.Close();
                txtReceive.AppendText("[" + dtn + "] " + "REF is Disconnected\n");
                txtReceive.AppendText("\n");
            }

            disconnectButton.Enabled = false;
            connectButton.Enabled = true;
            startBurnButton.Enabled = false;
            startTestButton.Enabled = false;
            sendButton.Enabled = false;
        }

        private void initEvtReceivedFlags()
        {
            isDUTReceivedTxTestEvt = 0;
            isDUTReceivedRxTestEvt = 0;
            isDUTReceivedTestEndEvt = 0;
            isREFReceivedTxTestEvt = 0;
            isREFReceivedRxTestEvt = 0;
            isREFReceivedTestEndEvt = 0;
            DUTreceivedPacketsNum = 0;
            REFreceivedPacketsNum = 0;
        }

        private void showTestResult(String result)
        {
            this.txtReceive.AppendText(result + Environment.NewLine + Environment.NewLine);
        }

        private void startButtonThreadCallback()
        {
            // LE Transmitter Test: channel 37, byte length 1, pattern 10101010
            byte[] HCIHCI_LE_Transmitter_Test = { 0x01, 0x1E, 0x20, 0x03, 0x25, 0x01, 0x02 };
            // HCI_LE_Receiver_Test: channel 37
            byte[] HCI_LE_Receiver_Test = { 0x01, 0x1D, 0x20, 0x01, 0x25 };
            // HCI_LE_Test_End
            byte[] HCI_LE_Test_End = { 0x01, 0x1F, 0x20, 0x00 };

            bool testresult = true;

            ///////////////////////////////////////////////// DUT Tx & REF Rx /////////////////////////////////////////////////////

            initEvtReceivedFlags();

            sendHexArray(HCI_LE_Receiver_Test, HCI_LE_Receiver_Test.Length, "HCI_LE_Receiver_Test", refPort);
            sendHexArray(HCIHCI_LE_Transmitter_Test, HCIHCI_LE_Transmitter_Test.Length, "HCIHCI_LE_Transmitter_Test", dutPort);
            // sleep for 1 sec
            Thread.Sleep(1000);
            sendHexArray(HCI_LE_Test_End, HCI_LE_Test_End.Length, "HCI_LE_Test_End", dutPort);
            sendHexArray(HCI_LE_Test_End, HCI_LE_Test_End.Length, "HCI_LE_Test_End", refPort);

            // sleep for 2 sec to wait for all events.
            Thread.Sleep(2000);

            if (isDUTReceivedTxTestEvt == 1 && isREFReceivedRxTestEvt == 1 && isDUTReceivedTestEndEvt == 1 && isREFReceivedTestEndEvt == 1)
            {
                txtReceive.BeginInvoke(new SetTextCallback(showTestResult), "===================================================");
                txtReceive.BeginInvoke(new SetTextCallback(showTestResult), "REF Received " + REFreceivedPacketsNum + " Packets");
                txtReceive.BeginInvoke(new SetTextCallback(showTestResult), "===================================================");

                if (REFreceivedPacketsNum < 1500)
                    testresult = false;
            }
            else
            {
                testresult = false;
            }

            ///////////////////////////////////////////////// REF Tx & DUT Rx /////////////////////////////////////////////////////

            initEvtReceivedFlags();

            sendHexArray(HCI_LE_Receiver_Test, HCI_LE_Receiver_Test.Length, "HCI_LE_Receiver_Test", dutPort);
            sendHexArray(HCIHCI_LE_Transmitter_Test, HCIHCI_LE_Transmitter_Test.Length, "HCIHCI_LE_Transmitter_Test", refPort);
            // sleep for 1 sec
            Thread.Sleep(1000);
            sendHexArray(HCI_LE_Test_End, HCI_LE_Test_End.Length, "HCI_LE_Test_End", refPort);
            sendHexArray(HCI_LE_Test_End, HCI_LE_Test_End.Length, "HCI_LE_Test_End", dutPort);

            // sleep for 2 sec to wait for all events.
            Thread.Sleep(2000);

            if (isREFReceivedTxTestEvt == 1 && isDUTReceivedRxTestEvt == 1 && isDUTReceivedTestEndEvt == 1 && isREFReceivedTestEndEvt == 1)
            {
                txtReceive.BeginInvoke(new SetTextCallback(showTestResult), "===================================================");
                txtReceive.BeginInvoke(new SetTextCallback(showTestResult), "DUT Received " + DUTreceivedPacketsNum + " Packets");
                txtReceive.BeginInvoke(new SetTextCallback(showTestResult), "===================================================");

                if (DUTreceivedPacketsNum < 1500)
                    testresult = false;
            }
            else
            {
                testresult = false;
            }

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

            if (testresult == true)
                txtReceive.BeginInvoke(new SetTextCallback(showTestResult), "Test Passed!");
            else
                txtReceive.BeginInvoke(new SetTextCallback(showTestResult), "Test Failed!");

            // initialize all flags
            initEvtReceivedFlags();
        }

        // Start Test Button
        private void startTestButton_Click(object sender, EventArgs e)
        {
            // make sure sport is already assigned a value by serialport_connect method.
            if (dutPort == null)
                return;

            System.Threading.Thread startButtonThread = new System.Threading.Thread(new System.Threading.ThreadStart(startButtonThreadCallback));
            startButtonThread.Start();
        }

        public void StartProc(string exePath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = exePath;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Minimized;
            Process.Start(startInfo);
        }

        private void startBurnButton_Click(object sender, EventArgs e)
        {
            startBurnButton.Enabled = false;
            StartProc("C:\\Program Files (x86)\\Texas Instruments\\SmartRF Tools\\Flash Programmer\\bin\\SmartRFProg.exe");
            // StartProc("%ProgramFiles%\\Texas Instruments\\SmartRF Tools\\Flash Programmer\\bin\\SmartRFProg.exe");
        }
    }
}
