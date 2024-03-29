﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using System.Text;


namespace chat_client
{
    public partial class ChatForm : Form
    {
        delegate void AppendTextDelegate(Control ctrl, string s);
        AppendTextDelegate _textAppender;
        Socket mainSock;
        IPAddress thisAddress;
        string nameID;

        public static class MsgBoxHelper
        {
            public static DialogResult Warn(string s,
            MessageBoxButtons buttons =
            MessageBoxButtons.OK,
            params object[] args)
            {
                return MessageBox.Show(f(s, args), "경고", buttons,
                MessageBoxIcon.Exclamation);
            }
            public static DialogResult Error(string s,
            MessageBoxButtons buttons =
            MessageBoxButtons.OK,
            params object[] args)
            {
                return MessageBox.Show(f(s, args), "오류", buttons,
                MessageBoxIcon.Error);
            }
            public static DialogResult Info(string s,
            MessageBoxButtons buttons = MessageBoxButtons.OK,
            params object[] args)
            {
                return MessageBox.Show(f(s, args), "알림", buttons,
                MessageBoxIcon.Information);
            }
            public static DialogResult Show(string s,
            MessageBoxButtons buttons = MessageBoxButtons.OK,
            params object[] args)
            {
                return MessageBox.Show(f(s, args), "알림", buttons, 0);
            }
            static string f(string s, params object[] args)
            {
                if (args == null) return s;
                return string.Format(s, args);
            }
        }

        // 비동기 작업에서 사용하는 소켓과 해당 작업에 대한 데이터 버퍼를 저장하는 클래스
        public class AsyncObject
        {
            public byte[] Buffer;
            public Socket WorkingSocket;
            public readonly int BufferSize;
            public AsyncObject(int bufferSize)
            {
                BufferSize = bufferSize;
                Buffer = new byte[BufferSize];
            }

            public void ClearBuffer()
            {
                Array.Clear(Buffer, 0, BufferSize);
            }
        }

        public ChatForm()
        {
            InitializeComponent();
            mainSock = new Socket(AddressFamily.InterNetwork,SocketType.Stream, ProtocolType.Tcp);
            _textAppender = new AppendTextDelegate(AppendText);
        }

        void AppendText(Control ctrl, string s)
        {
            if (ctrl.InvokeRequired) ctrl.Invoke(_textAppender, ctrl, s);
            else
            {
                string source = ctrl.Text;
                ctrl.Text = source + Environment.NewLine + s;
            }
        }

        

        

        void OnFormLoaded(object sender, EventArgs e)
        {
            if (thisAddress == null)
            {
                // 로컬호스트 주소를 사용한다.
                thisAddress = IPAddress.Loopback;
                txtAddress.Text = thisAddress.ToString();
            }
            else
            {
                thisAddress = IPAddress.Parse(txtAddress.Text);
            }
        }


        void OnConnectToServer(object sender, EventArgs e)
        {
            if (mainSock.Connected)
            {
                MsgBoxHelper.Error("이미 연결되어 있습니다!");
                return;
            }
            int port = 15000; //고정
            nameID = txtID.Text; //ID
            AppendText(txtHistory, string.Format("서버: @{0}, port: 15000, ID: @{1}",
                txtAddress.Text, nameID));
            try
            {
                mainSock.Connect(txtAddress.Text, port);
            }
            catch (Exception ex)
            {
                MsgBoxHelper.Error("연결에 실패했습니다!\n오류 내용: {0}",
                MessageBoxButtons.OK, ex.Message);
                return;
            }
            // 연결 완료되었다는 메세지를 띄워준다.
            AppendText(txtHistory, "서버와 연결되었습니다.");
            // 연결 완료, 서버에서 데이터가 올 수 있으므로 수신 대기한다.
            AsyncObject obj = new AsyncObject(4096);
            obj.WorkingSocket = mainSock;
            mainSock.BeginReceive(obj.Buffer, 0, obj.BufferSize, 0,
            DataReceived, obj);
        }

        void DataReceived(IAsyncResult ar)
        {
            // BeginReceive에서 추가적으로 넘어온 데이터를 AsyncObject 형식으로 변환한다.
            AsyncObject obj = (AsyncObject)ar.AsyncState;
            // 데이터 수신을 끝낸다.
            int received = obj.WorkingSocket.EndReceive(ar);
            // 받은 데이터가 없으면(연결끊어짐) 끝낸다.
            if (received <= 0)
            {
                obj.WorkingSocket.Close();
                return;
            }
            // 텍스트로 변환한다.
            string text = Encoding.UTF8.GetString(obj.Buffer);
            // : 기준으로 짜른다.
            // tokens[0] - 보낸 사람 ID, tokens[1] - 보낸 메세지
            string[] tokens = text.Split(':');
            string id = tokens[0];
            string msg = tokens[1];
            // 텍스트박스에 추가해준다.
            // 비동기식으로 작업하기 때문에 폼의 UI 스레드에서 작업을 해줘야 한다.
            // 따라서 대리자를 통해 처리한다.
            AppendText(txtHistory, string.Format("[받음]{0}: {1}", id, msg));
            // 클라이언트에선 데이터를 전달해줄 필요가 없으므로 바로 수신 대기한다.
            // 데이터를 받은 후엔 다시 버퍼를 비워주고 같은 방법으로 수신을 대기한다.
            obj.ClearBuffer();
            // 수신 대기
            obj.WorkingSocket.BeginReceive(obj.Buffer, 0, 4096, 0, DataReceived, obj);
        }

        void OnSendData(object sender, EventArgs e)
        {

            // 서버가 대기중인지 확인한다.
            if (!mainSock.IsBound)
            {
                MsgBoxHelper.Warn("서버가 실행되고 있지 않습니다!");
                return;
            }
            // 보낼 텍스트
            string tts = txtTTS.Text.Trim();
            if (string.IsNullOrEmpty(tts))
            {
                MsgBoxHelper.Warn("텍스트가 입력되지 않았습니다!");
                txtTTS.Focus();
                return;
            }
            // ID 와 메세지를 담도록 만든다.
            // 문자열을 utf8 형식의 바이트로 변환한다.
            byte[] bDts = Encoding.UTF8.GetBytes(nameID + ':' + tts);
            // 서버에 전송한다.
            mainSock.Send(bDts);
            // 전송 완료 후 텍스트박스에 추가하고, 원래의 내용은 지운다.
            AppendText(txtHistory, string.Format("[보냄]{0}: {1}", nameID,tts));
            txtTTS.Clear();
        }

        private void ChatForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                mainSock.Close();
            }
            catch { }
        }
    }

}

         
