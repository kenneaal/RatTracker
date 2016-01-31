﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Net.Http;
using System.Data;
using System.Threading;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Collections.Specialized;
using System.Web;
using SpeechLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocket4Net;
using System.Globalization;
using RatTracker_WPF.Models;
using System.Windows.Controls;
using System.Windows.Data;
using System.Threading.Tasks;

// May be able to drop these.
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Reflection;
using System.Diagnostics;
using System.Security.Permissions;
using System.ComponentModel;
using System.Net;

namespace RatTracker_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public class ratConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            List<string> myrats = value as List<string>;
            if (myrats == null)
            {
                return "I am a null rat.";
            }

            var matchedRats = MainWindow.Rats.Where(x => myrats.Contains(x.Key));
            var missingRats = myrats.Except(matchedRats.Select(x => x.Value._Id));
            foreach (string missingRat in missingRats)
            {
                Console.WriteLine("Cannot find rat '"+missingRat+"'");
            }

            IEnumerable<string> ratNames = matchedRats.Select(x => x.Value.CmdrName);

            string rats = string.Join(", ", ratNames);
            var index = rats.IndexOf(", ", StringComparison.Ordinal);
            
            if(index > 0)
            {
                string firstPart = rats.Substring(0, index);
                string secondPart = rats.Substring(index+2);

                rats = firstPart + " and " + secondPart;
            }

            return rats;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
                return "I am a negative rat";
        }
    }
    public class clientConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Console.WriteLine("Typeof value is " + value.GetType() + " and is " + value.ToString());
            Client myclient = value as Client;
            if (myclient == null)
                return "No client data";
            return myclient.cmdrname;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return "I am a negative client";
        }
    }
        public class Links
    {
        public string self { get; set; }
    }

    public class Meta
    {
        public int count { get; set; }
        public int limit { get; set; }
        public int offset { get; set; }
        public int total { get; set; }
    }

    public class Client
    {
        public string cmdrname { get; set; }
        public string nickname { get; set; }
    }

    public class Datum
    {
        public bool active { get; set; }
        public bool archive { get; set; }
        public Client client { get; set; }
        public bool codeRed { get; set; }
        public string createdAt { get; set; }
        public bool epic { get; set; }
        public string lastModified { get; set; }
        public bool open { get; set; }
        public string notes { get; set; }
        public string platform { get; set; }
        public List<string> quotes { get; set; }
        public List<string> rats { get; set; }
        public List<string> tempRats { get; set; }
        public bool successful { get; set; }
        public string system { get; set; }
        public string _id { get; set; }
        public float score { get; set; }
    }

    public class RootObject 
    {
        public Links links { get; set; }
        public Meta meta { get; set; }
        public List<Datum> data { get; set; }
    }
    public class netLogFile
    {
        public string netLogFileName;
        public DateTime lastChanged;
        public long fileOffset;
    }
        public class clientInfo
    {
        public string clientName { get; set; }
        public string clientID { get; set; }
        public string clientState { get; set; }
        public string clientIP { get; set; }
        public string sessionID { get; set; }
        public string clientSystem { get; set; }

    }
    public class Rescue
    {
        public bool archive { get; set; }
        public bool active { get; set; }
        public bool codeRed { get; set; }
        public string CMDRname { get; set; }
        public int createdAt { get; set; }
        public bool epic { get; set; }
        public bool open { get; set; }
        public int lastModified { get; set; }
        public string notes { get; set; }
        public string[] rats { get; set; }
        public bool successful { get; set; }
        public string system { get; set; }
        public string _id { get; set; }
        public int score { get; set; }

    }
    public class Friend
    {
        private static string GetElementValue(XContainer element, string name)
        {
            if ((element == null) || (element.Element(name) == null))
                return String.Empty;
            return element.Element(name).Value;
        }

        public string Name { get; private set; }
        public string Location { get; private set; }
        public string PrivGroup { get; private set; }
        public string CanWing { get; private set; }
        public Friend(XContainer friend)
        {
            Name = GetElementValue(friend, "Name");
            Location = GetElementValue(friend, "lastLocation");
            CanWing = GetElementValue(friend, "inviteToWing");
            PrivGroup = GetElementValue(friend, "privateGroup_id");
        }
    }
    public partial class MainWindow : Window
    {
        public bool stopNetLog;
        bool onDuty=false;
        string logDirectory = Properties.Settings.Default.NetLogPath;
        FileSystemWatcher watcher;
        FileInfo logFile;
        long fileOffset =0L;
        long fileSize;
        Thread threadLogWatcher;
        string parserState;
        clientInfo myClient = new clientInfo();
        APIWorker apworker;
        private static string wsURL = "ws://dev.api.fuelrats.com/";
        private static string edsmURL = "http://www.edsm.net/api-v1/";
        WebSocket ws;
        SpVoice voice = new SpVoice();
        string currentSystem;
        string scState;
        RootObject activeRescues = new RootObject();

        public MainWindow()
        {
            InitializeComponent();
            checkLogDirectory();
        }

        public static ConcurrentDictionary<string, Rat> Rats { get; } = new ConcurrentDictionary<string, Rat>();

        public void initWS()
        {
            appendStatus("Initializing WS connection to "+wsURL+"...");
            try
            {
                ws = new WebSocket(wsURL, "", WebSocketVersion.Rfc6455);
                ws.Error +=new EventHandler<SuperSocket.ClientEngine.ErrorEventArgs>(websocketClient_Error);
                ws.Opened += new EventHandler(websocketClient_Opened);
                ws.MessageReceived += new EventHandler<MessageReceivedEventArgs>(websocketClient_MessageReceieved);
                ws.Closed += new EventHandler(websocket_Client_Closed);
               
            }
            catch(Exception ex)
            {
                appendStatus("Well, that went tits up real fast: " + ex.Message);
            }
        }
        public void openWS()
        {
            ws.Open();

            appendStatus("WS client is " + ws.State.ToString());
        }

        public void sendWS(string action, IDictionary<string, string> data)
        {
            data.Add("action", action);
            string json = JsonConvert.SerializeObject(data);
            appendStatus("sendWS Serialized to: " + json);
            ws.Send(json);
        }

        private void websocket_Client_Closed(object sender, EventArgs e)
        {
            appendStatus("API WS Connection closed. Reconnecting...");
            openWS();
        }

        private void websocketClient_MessageReceieved(object sender, MessageReceivedEventArgs e)
        {
            dynamic data = JsonConvert.DeserializeObject(e.Message);
            switch ((string)data.type)
            {
                case "welcome":
                    appendStatus("API MOTD: " + data.data);
                    break;
                case "assignment":
                    appendStatus("Got a new assignment datafield: " + data.data);
                    break;
                default:
                    appendStatus("Unknown API type field: " + data.type + ": " + data.data);
                    break;
            }
            //appendStatus("Direct parse. Type:" + data.type + " Data:" + data.data);
        }

        public void websocketClient_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            appendStatus("Websocket: Exception thrown: " + e.Exception.Message);
        }
        public void websocketClient_Opened(object sender, EventArgs e)
        {
            appendStatus("Websocket: Connection to API established.");
            string message = JsonConvert.SerializeObject(new { cmd = "message", msg = "Fukken message" }, new JsonSerializerSettings() {  Formatting = Newtonsoft.Json.Formatting.None});
            ws.Send(message);
            IDictionary<string, string> data = new Dictionary<string, string>();
            data.Add("Foo", "bar");
            sendWS("assignment", data);
        }
        private void checkVerboseLogging()
        {
            /* if (CheckStationLogging())
            {
                appendStatus("Elite Dangerous is not logging system names!!! ");
                appendStatus("Add VerboseLogging=\"1\" to <Network> section in your config file and restart your client!");
            } */
        }


        public void appendStatus(string text)
        {
            if (statusDisplay.Dispatcher.CheckAccess())
            {
                statusDisplay.Text += "\n" + text;
                statusDisplay.ScrollToEnd();
                statusDisplay.CaretIndex = statusDisplay.Text.Length;
            }
            else
            {
                statusDisplay.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action<string>(appendStatus), text );
            }
            

        }
        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        private void onRenamed(object source, RenamedEventArgs e)
        {
            /* Stop watching the renamed file, look for new onChanged. */
        }

        private void parseFriendsList(string friendsList)
        {
            /* Sanitize the XML, it can break if over 40 friends long or so. */
            string xmlData;
            int count=0;
            xmlData = friendsList.Substring(friendsList.IndexOf("<") + friendsList.Length);
            appendStatus("Raw xmlData: " + xmlData);
            try {
                XDocument xdoc = XDocument.Parse(friendsList);
                appendStatus("Successful XML parse.");
                var rettest = xdoc.Element("OK");
                if(rettest != null)
                    appendStatus("Last friendslist action: " + xdoc.Element("OK").Value);
                IEnumerable<XElement> friends = xdoc.Descendants("item");
                foreach (var friend in friends)
                {
                    byte[] byteenc;
                    UnicodeEncoding unicoded = new UnicodeEncoding();
                    /* string preencode = Regex.Replace(friend.Element("name").Value, ".{2}", "\\x$0"); */
                    byteenc = StringToByteArray(friend.Element("name").Value);
                    //appendStatus("Friend:" + System.Text.Encoding.UTF8.GetString(byteenc));
                    count++;
                    if (friend.Element("pending").Value == "1")
                    {
                        appendStatus("Pending invite from CMDR " + System.Text.Encoding.UTF8.GetString(byteenc) + "detected!");
                        var disp = Dispatcher;
                        Brush frbrush=null;
                        voice.Speak("You have a pending friend invite from commander " + System.Text.Encoding.UTF8.GetString(byteenc));
                        disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => { frbrush = frButton.Background; }));
                        if (frbrush != Brushes.Green)
                        { /* Dear gods, you're a cheap hack, aren't you? */
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => frButton.Background = Brushes.Yellow));
                        }
                    }
                }
                /* Check the OK status field, which can contain useful information on successful FRs. */
                foreach (var element in xdoc.Descendants())
                {
                    if (element.Name == "OK")
                    {
                        appendStatus("Return code: " + xdoc.Element("data").Element("OK").Value);
                        if (xdoc.Element("data").Element("OK").Value.Contains("Invitation accepted"))
                        {
                            appendStatus("Friend request accepted!");
                            voice.Speak("Friend request accepted.");
                            var disp = Dispatcher;
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => frButton.Background = Brushes.Green));
                        }
                    }
                }
                appendStatus("Parsed " + count + " friends in FRXML.");

            }
            catch (Exception ex)
            {
                appendStatus("XML Parsing exception:" + ex.Message);
            }

        }
        private void parseWingInvite(string wingInvite)
        {
            string xmlData;
            xmlData = wingInvite.Substring(wingInvite.IndexOf("<") + wingInvite.Length);
            appendStatus("Raw xmlData: " + xmlData);
            try
            {
                XDocument xdoc = XDocument.Parse(wingInvite);
                appendStatus("Successful XML parse.");
                voice.Speak("Wing invite detected.");
                IEnumerable<XElement> wing = xdoc.Descendants("commander");
                foreach (var wingdata in wing)
                {
                    byte[] byteenc;
                    UnicodeEncoding unicoded = new UnicodeEncoding();
                    /* string preencode = Regex.Replace(friend.Element("name").Value, ".{2}", "\\x$0"); */
                    byteenc = StringToByteArray(wingdata.Element("name").Value);
                    appendStatus("Wingmember:" + System.Text.Encoding.UTF8.GetString(byteenc));
                    if (System.Text.Encoding.UTF8.GetString(byteenc) == myClient.clientName)
                    {
                        appendStatus("This data matches our current client! Storing information...");
                        myClient.clientID = wingdata.Element("id").Value;
                        appendStatus("Wingmember IP data:" + xdoc.Element("connectionDetails"));
                        string wingIPPattern = "IP4NAT:([0-9.]+):\\d+\\,";
                        Match wingMatch = Regex.Match(wingInvite, wingIPPattern, RegexOptions.IgnoreCase);
                        if (wingMatch.Success)
                        {
                            appendStatus("Successful IP data match: " + wingMatch.Groups[1]);
                            myClient.clientIP = wingMatch.Groups[1].Value;

                        }
                        /* If the friend request matches the client name, store his session ID. */
                        myClient.clientID = wingdata.Element("commander_id").Value;
                        myClient.sessionID = wingdata.Element("session_runid").Value;
                    }
                    
                }


            }
            catch (Exception ex)
            {
                appendStatus("Error in parseWingInvite: " + ex.Message);
                return;
            }
            return;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // Clean up our threads and exit.
            
        }

        private async void  checkLogDirectory()
        {
            Object col;
            if(logDirectory==null | logDirectory == "")
            {
                MessageBox.Show("Error: No log directory is specified, please do so before attempting to go on duty.");
                return;
            }
            if (!Directory.Exists(Properties.Settings.Default.NetLogPath))
            {
                MessageBox.Show("Error: Couldn't find E:D Netlog directory: " + Properties.Settings.Default.NetLogPath + ". Please ensure that it is correct in Settings.");
                return;
            }
            textBox.Text = logDirectory;
            statusDisplay.Text = "Beginning to watch " + logDirectory + " for changes...";
            if (watcher==null)
            {
                watcher = new FileSystemWatcher();
                watcher.Path = logDirectory;
                watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size;
                watcher.Filter = "*.log";
                watcher.Changed += new FileSystemEventHandler(onChanged);
                watcher.Created += new FileSystemEventHandler(onChanged);
                watcher.Deleted += new FileSystemEventHandler(onChanged);
                watcher.Renamed += new RenamedEventHandler(onRenamed);
                watcher.EnableRaisingEvents = true;

            }
            DirectoryInfo tempDir=new DirectoryInfo(logDirectory);
            logFile = (from f in tempDir.GetFiles("*.log") orderby f.LastWriteTime descending select f).First();
            appendStatus("Started watching file " + logFile.FullName);
            checkClientConn(logFile.FullName);
            var logindata = new List<KeyValuePair<string, string>>();
            logindata.Add(new KeyValuePair<string,string>("email", "mecha@squeak.net"));
            logindata.Add(new KeyValuePair<string,string>("password", "password"));
            apworker = new APIWorker();
            appendStatus("Call to APIworker returning :"+apworker.connectAPI().ToString());
            //NameValueCollection col = await apworker.queryAPI("login", new List<KeyValuePair<string, string>>());
            col = await apworker.sendAPI("login", logindata);
            if (col.ToString() == null)
                appendStatus("Login returned NULL");
            else
            {
                //appendStatus("From col I have :" + col.ToString());
                appendStatus("Login returned: " + col);
                
            }
            appendStatus("I'm after queryAPI:"+col.ToString());
            initWS();
            openWS();
            readLogfile(logFile.FullName);
        }
        private void ProcessAPIResponse(IAsyncResult result)
        {
            this.appendStatus("Whaddaya know, ProcessAPIResponse got called!");
        }
        private void checkClientConn(string lf)
        {
            bool stopSnooping=false;
            appendStatus("Detecting client connectivity...");
            try
            {
                Dispatcher disp = Dispatcher;
                using (StreamReader sr = new StreamReader(new FileStream(lf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
                {
                    string line;
                    int count = 0;
                    while (stopSnooping != true && sr.Peek() != -1 && count<10000)
                    {
                        count++;
                        line = sr.ReadLine();
                        // TODO: Populate WAN, STUN and Turn server labels. Make cleaner TURN detection.
                        if(line.Contains("WAN:"))
                        {
                            appendStatus("E:D is configured to listen on "+line);
                        }
                        if(line.Contains("failed to initialise upnp"))
                        {
                            appendStatus("CRITICAL: E:D has failed to establish a upnp port mapping, but E:D is configured to use upnp. Disable upnp in netlog if you have manually mapped ports.");
                        }

                        if(line.Contains("Turn State: Ready"))
                        {
                            appendStatus("Client has a valid TURN connection established.");
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => connTypeLabel.Content = "TURN routed"));
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => turnButton.Background = Brushes.Green));

                        }
                        if (line.Contains("this machine after STUN reply"))
                        {
                            appendStatus("STUN has mapped us to address.");
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => connTypeLabel.Content = "STUN enabled NAT"));
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => stunButton.Background = Brushes.Green));

                        }
                        if (line.Contains("Sync Established"))
                        {
                            appendStatus("Sync Established.");
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => syncButton.Background = Brushes.Green));

                        }
                        if (line.Contains("ConnectToServerActivity:StartRescueServer"))
                        {
                            appendStatus("E:D has established a connection and client is in main menu. Ending early netlog parse.");
                            stopSnooping = true;
                        }
                        if (line.Contains("Symmetrical"))
                        {
                            appendStatus("CRITICAL: E:D has detected symmetrical NAT on this connection. This may make it difficult for you to instance with clients!");
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => connTypeLabel.Content = "Symmetrical NAT"));
                            disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => directButton.Background = Brushes.Red));
                        }

                    }
                    appendStatus("Parsed " + count + " lines to derive client info.");
                    if (stopSnooping == false)
                    {
                        appendStatus("Client connectivity detection complete. You have a direct port mapped address that E:D can use, and should be connectable.");

                    }
                }
            }
            catch (Exception ex)
            {
                appendStatus("Exception in checkClientConn:" + ex.Message);
                return;
            }

        }
        private void readLogfile(string lf)
        {
            try
            {
                using (StreamReader sr = new StreamReader(new FileStream(lf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
                {
                    if (fileOffset == 0L)
                    {
                        appendStatus("First peek...");
                        if (sr.BaseStream.Length > 5000)
                        {
                            sr.BaseStream.Seek(-5000, SeekOrigin.End); /* First peek into the file, rewind a bit and scan from there. */
                        }
                    }
                    else
                    {
                        sr.BaseStream.Seek(this.fileOffset, SeekOrigin.Begin);
                    }
                    string line;
                    int count=0;
                    while (sr.Peek() != -1)
                    {
                        count++;
                        line = sr.ReadLine();
                        parseLine(line);
                    }
                    //appendStatus("Parsed " + count + " new lines. Old fileOffset was "+fileOffset+" and length was "+logFile.Length);
                }
            }
            catch (Exception ex)
            {
                appendStatus("Exception in readLogFile: " + ex.Message);
                return;
            }
        }

        private void  parseLine(string line)
        {
 /*           if (parserState == "ISLAND")
            {
                if (line.Contains(myClient.sessionID.ToString()) && scState=="Normalspace")
                {
                    appendStatus("Normalspace Instance match! " + line);
                    Dispatcher disp = Dispatcher;
                    disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => wrButton.Background = Brushes.Green));
                    //voice.Speak("Successful normal space instance with client.");
                }
            } */
            string reMatchSystem = ".*?(System:).*?\\(((?:[^)]+)).*?\\)";
            Match match = Regex.Match(line, reMatchSystem, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                appendStatus("System change: " + match.Groups[2].Value + ".");
                triggerSystemChange(match.Groups[2].Value);
            }
            string reMatchPlayer = "\\{.+\\} (\\d+) x (\\d+).*\\(\\(([0-9.]+):\\d+\\)\\)Name (.+)$";
            Match frmatch = Regex.Match(line, reMatchPlayer, RegexOptions.IgnoreCase);
            if (frmatch.Success)
            {
                appendStatus("Successful identity match! ID: " + frmatch.Groups[1] + " IP:" + frmatch.Groups[3]);
            }
            if (line.Contains("FriendsRequest"))
            {
                parserState = "xml";
                appendStatus("Enter XML parse state. Full line: " + line);
                appendStatus("Received FriendsList update, ready to parse...");
            }
            if (line.Contains("<data>"))
            {
                appendStatus("Line sent to XML parser");
                parseFriendsList(line);
            }
            /* Look, we're doing nothing! */
            if (line.Contains("</data>"))
            {
                appendStatus("Exit XML parsing mode.");
                //parserState = "normal";
            }
            if (line.Contains("<FriendWingInvite>"))
            {
                appendStatus("Wing invite detected, parsing...");
                parseWingInvite(line);
                Dispatcher disp = Dispatcher;
                disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => wrButton.Background = Brushes.Yellow));

            }
            if (line.Contains("JoinSession:WingSession:") && line.Contains(myClient.clientIP.ToString()))
            {
                appendStatus("Prewing communication underway...");
            }
            if (line.Contains("TalkChannelManager::OpenOutgoingChannelTo") && line.Contains(myClient.clientIP.ToString()))
            {
                appendStatus("Wing established, opening voice comms.");
                //voice.Speak("Wing established.");
                Dispatcher disp = Dispatcher;
                disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => wrButton.Background = Brushes.Green));

            }
            if(line.Contains("ListenResponse->Listening (SUCCESS: User has responded via local talkchannel)"))
            {
                appendStatus("Voice communications established.");
            }
            if (line.Contains("NormalFlight") && scState =="Supercruise")
            {
                scState = "Normalspace";
                appendStatus("Drop to normal space detected.");
                //voice.Speak("Dropping to normal space.");
            }
            if(line.Contains("Supercruise") && scState == "Normalspace")
            {
                scState = "Supercruise";
                appendStatus("Entering supercruise.");
                //voice.Speak("Entering supercruise.");
            }
            if(line.Contains("CLAIMED ------------vvv"))
            {
                appendStatus("Island claim message detected, parsing members...");
                //parserState = "ISLAND";
            }
            if (line.Contains("claimed ------------^^^"))
            {
                appendStatus("End of island claim member list. Resuming normal parse.");
                //parserState = "NORMAL";
            }
            if (line.Contains("SESJOINED"))
            {
                appendStatus("Session join message seen.");
            }
            if (line.Contains("JoinSession:BeaconSession") && line.Contains(myClient.clientIP.ToString()))
            {
                appendStatus("Client's Beacon in sight.");
                
            }
        }

        private async void triggerSystemChange(string value)
        {
            Dispatcher disp = Dispatcher;
            if (value == currentSystem)
                return;
            try
            {
                using (var client = new HttpClient())
                {
                    var content = new UriBuilder(edsmURL + "systems?sysname="+value+"&coords=1");
                    content.Port = -1;
                    var query = HttpUtility.ParseQueryString(content.Query);
                    content.Query = query.ToString();
                    appendStatus("Built query string:" + content.ToString());
                    var response = await client.GetAsync(content.ToString());
                    response.EnsureSuccessStatusCode();
                    var responseString = await response.Content.ReadAsStringAsync();
                    appendStatus("Response string:" + responseString);
                    NameValueCollection temp = new NameValueCollection();
                    dynamic m = JsonConvert.DeserializeObject(responseString);
                    //voice.Speak("Welcome to " + value);
                    currentSystem = value;
                    await disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => systemNameLabel.Content = value));
                    if (responseString.Contains("-1"))
                        await disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => systemNameLabel.Foreground = Brushes.Red));
                    else
                        await disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => systemNameLabel.Foreground = Brushes.Yellow));
                    if(responseString.Contains("coords"))
                        await disp.BeginInvoke(DispatcherPriority.Normal, (Action)(() => systemNameLabel.Foreground = Brushes.Green));
                    return;
                }
            }
            catch (Exception ex) {
                appendStatus("Exception in triggerSystemChange: " + ex.Message);
                return;
            }
        }

        private void onChanged(object source, FileSystemEventArgs e)
        {
            logFile = new FileInfo(e.FullPath);
            /* Handle changed events */
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            if (onDuty == false)
            {
                button.Content = "On Duty";
                onDuty = true;
                watcher.EnableRaisingEvents = true;
                statusDisplay.Text += "\nStarted watching for events in netlog.";
                button.Background = Brushes.Green; 
                stopNetLog = false;
                threadLogWatcher = new System.Threading.Thread(new System.Threading.ThreadStart(netLogWatcher));
                threadLogWatcher.Name = "Netlog watcher";
                threadLogWatcher.Start();
                
                /* image.Source = new BitmapImage(RatTracker_WPF.Properties.Resources.yellow_light); */
                clientName.Text = "Absolver";
                systemName.Text = "Sagittarius A*";
             }
            else
            {
                button.Content = "Off Duty";
                onDuty = false;
                watcher.EnableRaisingEvents = false;
                statusDisplay.Text += "\nStopped watching for events in netlog.";
                button.Background = Brushes.Red;
                stopNetLog = true;
            }
        }
        private void netLogWatcher()
        {
            appendStatus("Netlogwatcher started.");
            bool logChanged = false;
            try
            {
                while (!stopNetLog)
                {
                    Thread.Sleep(2000);
                    
                    if (logChanged == false)
                    {
                        FileInfo fi = new FileInfo(logFile.FullName);
                        //appendStatus("Netlog tick with status false. LFL:"+fi.Length+ "Filesize: "+fileSize);
                        if (fi.Length != fileSize)
                        {
                            //appendStatus("Log file size increased.");
                            readLogfile(fi.FullName); /* Maybe a poke on the FS is enough to wake watcher? */
                            fileOffset = fi.Length;
                            fileSize = fi.Length;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("Netlog exception: " + ex.Message);
            }
        }
        private void Main_Menu_Click(object sender, RoutedEventArgs e)
        {
            /* Fleh? */
        }

        private void frButton_Click(object sender, RoutedEventArgs e)
        {
            if (frButton.Background == Brushes.Red)
            {
                frButton.Background = Brushes.Green;
                appendStatus("Sending Friend Request acknowledgement.");
                IDictionary<string, string> data = new Dictionary<string, string>();
                data.Add("ReceivedFR", "true");
                sendWS("FriendRequest",data);
                /* image.Source = new BitmapImage(RatTracker_WPF.Properties.Resources.yellow_light); */
            }
            else
            {
                appendStatus("Cancelling FR status.");
                frButton.Background = Brushes.Red;
                IDictionary<string, string> data = new Dictionary<string, string>();
                data.Add("ReceivedFR", "false");
                sendWS("FriendsRequest",data);
            }
        }

        private void wrButton_Click(object sender, RoutedEventArgs e)
        {
            if (wrButton.Background == Brushes.Red)
            {
                appendStatus("Sending Wing Request acknowledgement.");
                IDictionary<string, string> data = new Dictionary<string, string>();
                data.Add("ReceivedWR", "true");
                sendWS("WingRequest", data);
                wrButton.Background = Brushes.Green;
                /* image.Source = new BitmapImage(RatTracker_WPF.Properties.Resources.yellow_light); */
            }
            else
            {
                appendStatus("Cancelled WR status.");
                IDictionary<string, string> data = new Dictionary<string, string>();
                data.Add("ReceivedWR", "false");
                sendWS("WingRequest", data);

                wrButton.Background = Brushes.Red;
            }
        }

        private void bcnButton_Click(object sender, RoutedEventArgs e)
        {
            if (bcnButton.Background == Brushes.Red)
            {
                appendStatus("Sending Beacon acknowledgement.");
                bcnButton.Background = Brushes.Green;
                /* image.Source = new BitmapImage(RatTracker_WPF.Properties.Resources.yellow_light); */
            }
            else
            {
                appendStatus("Cancelling Beacon status.");
                bcnButton.Background = Brushes.Red;
            }
        }

        private void instButton_Click(object sender, RoutedEventArgs e)
        {
            if (instButton.Background == Brushes.Red)
            {
                appendStatus("Sending Good Instance message.");
                instButton.Background = Brushes.Green;
                /* image.Source = new BitmapImage(RatTracker_WPF.Properties.Resources.yellow_light); */
            }
            else
            {
                appendStatus("Cancelling good instance message.");
                instButton.Background = Brushes.Red;
            }
        }

        private void fueledButton_Click(object sender, RoutedEventArgs e)
        {
            if (fueledButton.Background == Brushes.Red)
            {
                appendStatus("Reporting fueled status, requesting paperwork link...");
                fueledButton.Background = Brushes.Green;
                /* image.Source = new BitmapImage(RatTracker_WPF.Properties.Resources.yellow_light); */
            }
            else
            {
                appendStatus("Fueled status now negative.");
                fueledButton.Background = Brushes.Red;
            }
            appendStatus("Sending fake rescue request!");
            IDictionary<string,string> req = new Dictionary<string, string>();
            req.Add("open", "true");
            sendWS("rescues", req);
        }

        private void currentButton_Click(object sender, RoutedEventArgs e)
        {
            appendStatus("Setting client location to current system: Fuelum");
            systemName.Text = "Fuelum";
        }

        private async void updateButton_Click(object sender, RoutedEventArgs e)
        {
            appendStatus("Trying to fetch rescues...");
            Dictionary<string,string> data = new Dictionary<string, string>();
            //data.Add("rats", "56a8fcc7abdd7cc91123fd25");
            data.Add("open", "true");
            String col = await apworker.queryAPI("rescues",data);
            
            if (col == null)
                appendStatus("No COL returned from Rescues.");
            else
            {
                appendStatus("Got a COL from Rescues query!");
                RootObject rescues = JsonConvert.DeserializeObject<RootObject>(col);
                await GetMissingRats(rescues);

                rescueGrid.ItemsSource = rescues.data;
                rescueGrid.AutoGenerateColumns = false;

                foreach (DataGridColumn column in rescueGrid.Columns)
                {
                    appendStatus("Column:" + column.Header);
                    if ((string)column.Header == "rats")
                        appendStatus("It's the rats.");
                }
            }
        }

        // TODO: Move to API?
        private async Task GetMissingRats(RootObject rescues)
        {
            IEnumerable<string> ratIdsToGet = new List<string>();

            var datas = from d in rescues.data
                select d.rats;
            ratIdsToGet = datas.Aggregate(ratIdsToGet, (current, list) => current.Concat(list));
            ratIdsToGet = ratIdsToGet.Distinct().Except(Rats.Values.Select(x => x._Id));

            foreach (var ratId in ratIdsToGet)
            {
                var response = await apworker.queryAPI("rats", new Dictionary<string, string> { { "_id", ratId }, { "limit", "1" } });
                JObject jsonRepsonse = JObject.Parse(response);
                List<JToken> tokens = jsonRepsonse["data"].Children().ToList();
                var rat = JsonConvert.DeserializeObject<Rat>(tokens[0].ToString());
                Rats.TryAdd(ratId, rat);

                Console.WriteLine("Got name for " + ratId + ": " + rat.CmdrName);
            }
        }

        private void startButton_Click(object sender, RoutedEventArgs e)
        {
            appendStatus("Started tracking new client " + clientName.Text);
            myClient.clientName = clientName.Text;
            frButton.Background = Brushes.Red;
            wrButton.Background = Brushes.Red;
            instButton.Background = Brushes.Red;
            bcnButton.Background = Brushes.Red;
            fueledButton.Background = Brushes.Red;

        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            wndSettings swindow = new wndSettings();
            swindow.Show();
        }
    }
}
