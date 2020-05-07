using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Web;
using MySql.Data.MySqlClient;
using Bend.Util;


// offered to the public domain for any use with no restriction
// and also with no warranty of any kind, please enjoy. - David Jeske. 

// simple HTTP explanation
// http://www.jmarshall.com/easy/http/

namespace Bend.Util {

    public class HttpProcessor {
        public TcpClient socket;        
        public HttpServer srv;

        private Stream inputStream;
        public StreamWriter outputStream;

        public String http_method;
        public String http_url;
        public String http_protocol_versionstring;
        public Hashtable httpHeaders = new Hashtable();


        private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient s, HttpServer srv) {
            this.socket = s;
            this.srv = srv;                   
        }
        

        private string streamReadLine(Stream inputStream) {
            int next_char;
            string data = "";
            while (true) {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }            
            return data;
        }
        public void process() {                        
            // we can't use a StreamReader for input, because it buffers up extra data on us inside it's
            // "processed" view of the world, and we want the data raw after the headers
            inputStream = new BufferedStream(socket.GetStream());

            // we probably shouldn't be using a streamwriter for all output from handlers either
            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
            try {
                parseRequest();
                readHeaders();
                if (http_method.Equals("GET")) {
                    handleGETRequest();
                } else if (http_method.Equals("POST")) {
                    handlePOSTRequest();
                }
            } catch (Exception e) {
                Console.WriteLine("Exception: " + e.ToString());
                writeFailure();
            }
            outputStream.Flush();
            // bs.Flush(); // flush any remaining output
            inputStream = null; outputStream = null; // bs = null;            
            socket.Close();             
        }

        public void parseRequest() {
            String request = streamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3) {
                throw new Exception("invalid http request line");
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];
            http_protocol_versionstring = tokens[2];

            Console.WriteLine("starting: ");//("starting: " + request);
        }

        public void readHeaders() {
            //Console.WriteLine("readHeaders()");
            String line;
            while ((line = streamReadLine(inputStream)) != null) {
                if (line.Equals("")) {
                   //Console.WriteLine("got headers");
                    return;
                }
                
                int separator = line.IndexOf(':');
                if (separator == -1) {
                    throw new Exception("invalid http header line: " + line);
                }
                String name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' ')) {
                    pos++; // strip any spaces
                }
                    
                string value = line.Substring(pos, line.Length - pos);
                //Console.WriteLine("header: {0}:{1}",name,value);
                httpHeaders[name] = value;
            }
        }

        public void handleGETRequest() {
            srv.handleGETRequest(this);
        }

        private const int BUF_SIZE = 4096;
        public void handlePOSTRequest() {
            // this post data processing just reads everything into a memory stream.
            // this is fine for smallish things, but for large stuff we should really
            // hand an input stream to the request processor. However, the input stream 
            // we hand him needs to let him see the "end of the stream" at this content 
            // length, because otherwise he won't know when he's seen it all! 

            //Console.WriteLine("get post data start");
            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length")) {
                 content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                 if (content_len > MAX_POST_SIZE) {
                     throw new Exception(
                         String.Format("POST Content-Length({0}) too big for this simple server",
                           content_len));
                 }
                 byte[] buf = new byte[BUF_SIZE];              
                 int to_read = content_len;
                 while (to_read > 0) {  
                     //Console.WriteLine("starting Read, to_read={0}",to_read);

                     int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                    // Console.WriteLine("read finished, numread={0}", numread);
                     if (numread == 0) {
                         if (to_read == 0) {
                             break;
                         } else {
                             throw new Exception("client disconnected during post");
                         }
                     }
                     to_read -= numread;
                     ms.Write(buf, 0, numread);
                    //Console.WriteLine("{0}");
                }
                 ms.Seek(0, SeekOrigin.Begin);
            }
            //Console.WriteLine("get post data end");
            srv.handlePOSTRequest(this, new StreamReader(ms));

        }

        public void writeSuccess(string content_type="text/html") {
            outputStream.WriteLine("HTTP/1.0 200 OK");            
            outputStream.WriteLine("Content-Type: " + content_type);
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }

        public void writeFailure() {
            outputStream.WriteLine("HTTP/1.0 404 File not found");
            outputStream.WriteLine("Connection: close");
            outputStream.WriteLine("");
        }
    }

    public abstract class HttpServer {

        protected int port;
        TcpListener listener;
        bool is_active = true;
       
        public HttpServer(int port) {
            this.port = port;
        }

        public void listen() {
            listener = new TcpListener(port);
            listener.Start();
            while (is_active) {                
                TcpClient s = listener.AcceptTcpClient();
                HttpProcessor processor = new HttpProcessor(s, this);
                Thread thread = new Thread(new ThreadStart(processor.process));
                thread.Start();
                Thread.Sleep(1);
            }
        }

        public abstract void handleGETRequest(HttpProcessor p);
        public abstract void handlePOSTRequest(HttpProcessor p, StreamReader inputData);
    }

    public class MyHttpServer : HttpServer {
        public MyHttpServer(int port)
            : base(port) {
        }
        public override void handleGETRequest (HttpProcessor p)
		{

			if (p.http_url.Equals ("/Test.png")) {
				Stream fs = File.Open("../../Test.png",FileMode.Open);

				p.writeSuccess("image/png");
				fs.CopyTo (p.outputStream.BaseStream);
				p.outputStream.BaseStream.Flush ();
			}
            var buf_data = p.http_url.Split('=');

            string token = "";
            string username = "";
            if (buf_data[0] == "/?user_name")
            {
                token = buf_data[2].ToString();
                Console.WriteLine("token: " + token);

                int index = buf_data[1].IndexOf("&");
                if (index > 0)
                    username = buf_data[1].Substring(0, index);
                Console.WriteLine("username: " + username);

                //MySqlConnection conn = DBUtils.GetDBConnection();
                //conn.Open();
                ////Фиксируем в log in db
                //string sql = string.Format("update btk.Users set user_token='"+token+"' where username='"+username+"';");
                //// объект для выполнения SQL-запроса
                //MySqlCommand command = new MySqlCommand(sql, conn);
                //// объект для чтения ответа сервера
                //MySqlDataReader reader = command.ExecuteReader();
                //reader.Close();
                //conn.Close();

            }


            p.writeSuccess();
            p.outputStream.WriteLine("<html><body><h1>test server</h1>");
            p.outputStream.WriteLine("Current Time: " + DateTime.Now.ToString());
            p.outputStream.WriteLine("url : {0}", p.http_url);

            p.outputStream.WriteLine("<form method=post action=/form>");
            p.outputStream.WriteLine("<input type=text name=foo value=foovalue>");
            p.outputStream.WriteLine("<input type=submit name=bar value=barvalue>");
            p.outputStream.WriteLine("</form>");
        }

        public override void handlePOSTRequest(HttpProcessor p, StreamReader inputData) {
            //Console.WriteLine("POST request: {0}", p.http_url);
            string data = HttpUtility.UrlDecode(inputData.ReadToEnd());

            string[] buf_data = data.Split('&');

            var unit_name = buf_data[0].ToUpper();
            var unit_id = buf_data[1];
            var notification = buf_data[2];
            var curr_time = buf_data[3];
            var msg_time = buf_data[4];
            var location = buf_data[5];
            var last_location = buf_data[6];
            var speed = buf_data[7];
            var product = buf_data[8];
            var type_alarm = buf_data[9];

            MySqlConnection conn = DBUtils.GetDBConnection();
            conn.Open();
            //Фиксируем в log in db
            string sql = string.Format("INSERT INTO btk.notification(unit_name, unit_id, notification, curr_time, msg_time, location, last_location, speed, product, type_alarm, Users_idUsers, Status) VALUES('" + unit_name + "','" + unit_id + "','" + notification + "','" + curr_time + "','" + msg_time + "','','"+ last_location + "','','" + product + "','" + type_alarm + "', '8', 'Відкрито')");
            // объект для выполнения SQL-запроса
            MySqlCommand command = new MySqlCommand(sql, conn);
            // объект для чтения ответа сервера
            MySqlDataReader reader = command.ExecuteReader();
            reader.Close();
            conn.Close();

            
            int count_notif=0;

            count_notif++;

            Console.WriteLine("End for = " + count_notif + ", " + unit_id + ", " + unit_name + ", " + notification + ", " +DateTime.Now.ToString() );

            //p.writeSuccess();
            //p.outputStream.WriteLine("<html><body><h1>test server</h1>");
            //p.outputStream.WriteLine("<a href=/test>return</a><p>");
            //p.outputStream.WriteLine("postbody: <pre>{0}</pre>", data);
        }

        private void write2bd(string log_txt)//write log to DB
        {
            MySqlConnection conn = DBUtils.GetDBConnection();
            conn.Open();
            //Фиксируем в log in db
            string sql = string.Format("INSERT INTO btk.notification(unit_name, unit_id, notification, curr_time, msg_time, location, last_location, speed) VALUES('" + log_txt + "','" + "srv" + "')");
            // объект для выполнения SQL-запроса
            MySqlCommand command = new MySqlCommand(sql, conn);
            // объект для чтения ответа сервера
            MySqlDataReader reader = command.ExecuteReader();
            reader.Close();
            conn.Close();
        }
    }

    public class TestMain {
        public static int Main(String[] args) {
            HttpServer httpServer;
            if (args.GetLength(0) > 0) {
                httpServer = new MyHttpServer(Convert.ToInt16(args[0]));
            } else {
                httpServer = new MyHttpServer(57001);
            }
            Thread thread = new Thread(new ThreadStart(httpServer.listen));
            thread.Start();
            return 0;
        }

    }

    class DBUtils
    {
        public static MySqlConnection GetDBConnection()
        {
            string host = "10.44.30.32";
            int port = 3306;
            string database = "btk";
            string username = "lozik";
            string password = "lozik";
            string SslMode = "none";
            string charset = "utf8";


            return DBMysqlUtils.GetDBConnection(host, port, database, username, password, SslMode, charset);
        }
    }

    class DBMysqlUtils
    {
        public static MySqlConnection
             GetDBConnection(string host, int port, string database, string username, string password, string SslMode, string charset)
        {
            // Connection String.
            String connString = "Server=" + host + ";Database=" + database
                + ";port=" + port + ";User Id=" + username + ";password=" + password + ";password=" + password + ";SslMode=" + SslMode + ";charset=" + charset;

            MySqlConnection conn = new MySqlConnection(connString);

            return conn;
        }
    }

}