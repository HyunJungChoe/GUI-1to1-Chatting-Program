﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;


namespace chat_server
{
    

    public partial class ChatForm : Form
    {
        Socket mainSock; // 서버소켓
        IPAddress thisAddress;
        List<Socket> connectedClients;
        //클라이언트들의 소켓 정보를 담을 자료구조

        delegate void AppendTextDelegate
        (Control ctrl, string s);
        AppendTextDelegate _textAppender;
        // GUI 개체 활용을 위한 델리게이트

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
            _textAppender = new AppendTextDelegate(AppendText);

            InitializeComponent();
            mainSock = new Socket(AddressFamily.InterNetwork,
            SocketType.Stream, ProtocolType.Tcp);
            connectedClients = new List<Socket>();
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

        void AcceptCallback(IAsyncResult ar)
        {
            // 클라이언트의 연결 요청을 수락한다.
            Socket client = mainSock.EndAccept(ar);
            // 또 다른 클라이언트의 연결을 대기한다.
            mainSock.BeginAccept(AcceptCallback, null);
            AsyncObject obj = new AsyncObject(4096);
            obj.WorkingSocket = client;
            // 연결된 클라이언트 리스트에 추가해준다.
            connectedClients.Add(client);
            // 텍스트박스에 클라이언트가 연결되었다고 써준다.
            AppendText(txtHistory, string.Format
            ("클라이언트 (@ {0})가 연결되었습니다.",
            client.RemoteEndPoint));
            // 클라이언트의 데이터를 받는다.
            client.BeginReceive(obj.Buffer, 0, 4096, 0,
            DataReceived, obj);
        }

        void DataReceived(IAsyncResult ar)
        {
            // BeginReceive에서 추가적으로 넘어온 데이터를
            // AsyncObject 형식으로 변환한다.
            AsyncObject obj = (AsyncObject)ar.AsyncState;
            // as AsyncObject로 해도 됨
            // 데이터 수신을 끝낸다.
            int received = obj.WorkingSocket.EndReceive(ar);
            // 받은 데이터가 없으면(연결끊어짐) 끝낸다.
            if (received <= 0)
            {
                obj.WorkingSocket.Disconnect(false);
                obj.WorkingSocket.Close();
                return;
            }
            // 텍스트로 변환한다.
            string text =
            Encoding.UTF8.GetString(obj.Buffer);
            // : 기준으로 짜른다.
            // tokens[0] - 보낸 사람 ID
            // tokens[1] - 보낸 메세지
            string[] tokens = text.Split(':');
            string id = tokens[0];
            string msg = tokens[1];
            // 텍스트박스에 추가해준다.
            // 비동기식으로 작업하기 때문에 폼의 UI 스레드에서 작업을 해줘야 한다.
            // 따라서 대리자를 통해 처리한다.
            AppendText(txtHistory, string.Format
            ("[받음]{0}: {1}", id, msg));
            // 클라이언트에게 다시 전송
            obj.WorkingSocket.Send(obj.Buffer);
            // 데이터를 받은 후엔 다시 버퍼를 비워주고 같은 방법으로 수신을 대기한다.
            obj.ClearBuffer();
            // 수신 대기
            obj.WorkingSocket.BeginReceive
            (obj.Buffer, 0, 4096, 0, DataReceived, obj);
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
            // 문자열을 utf8 형식의 바이트로 변환한다.
            byte[] bDts = Encoding.UTF8.GetBytes("Server" + ':' + tts);
            // 연결된 모든 클라이언트에게 전송한다.
            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                Socket socket = connectedClients[i];
                try { socket.Send(bDts); }
                catch
                {
                    // 오류 발생하면 전송 취소하고 리스트에서 삭제한다.
                    try { socket.Dispose(); } catch { }
                    connectedClients.RemoveAt(i);
                }
            }
            // 전송 완료 후 텍스트박스에 추가하고, 원래의 내용은 지운다.
            AppendText(txtHistory, string.Format("[보냄]server: {0}", tts));
            txtTTS.Clear();
        }

        void BeginStartServer(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(txtPort.Text, out port))
            {
                MsgBoxHelper.Error("포트 번호가 잘못 입력되었거나 입력되지 않았습니다.");
                txtPort.Focus();
                txtPort.SelectAll();
                return;
            }
            if (thisAddress == null)
            { // 로컬호스트 주소를 사용한다.
                thisAddress = IPAddress.Loopback;
                txtAddress.Text = thisAddress.ToString();
            }
            else
            {
                thisAddress = IPAddress.Parse(txtAddress.Text);
            }
            // 서버에서 클라이언트의 연결 요청을 대기하기 위해
            // 소켓을 열어둔다.
            IPEndPoint serverEP = new IPEndPoint(thisAddress, port);
            mainSock.Bind(serverEP);
            mainSock.Listen(10);
            AppendText(txtHistory, string.Format("서버 시작: @{0}", serverEP));
            // 비동기적으로 클라이언트의 연결 요청을 받는다.
            mainSock.BeginAccept(AcceptCallback, null);
        }

        void OnFromLoaded(object sender, EventArgs e)
        {
            IPHostEntry he = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress addr in he.AddressList)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                    AppendText(txtHistory, addr.ToString());
            }
            // 주소가 없다면..
            if (thisAddress == null)
            { // 로컬호스트 주소를 사용한다.
                thisAddress = IPAddress.Loopback;
                txtAddress.Text = thisAddress.ToString();
            }
            else
                thisAddress = IPAddress.Parse(txtAddress.Text);

        }

        private void ChatForm_Closing(object sender, FormClosingEventArgs e)
        {
            try
            {
                mainSock.Close();
            }
            catch { }
        }
    }
   


}



