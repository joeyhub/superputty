using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.Web;
using log4net;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using WeifenLuo.WinFormsUI.Docking;

namespace SuperPutty.Data
{
    namespace Legacy1040
    {
        public class SessionData
        {
            [XmlAttribute]
            public string SessionId = null;
            [XmlAttribute]
            public string SessionName = null;
            [XmlAttribute]
            public string ImageKey = null;
            [XmlAttribute]
            public string Host = null;
            [XmlAttribute]
            public int Port = 0;
            [XmlAttribute]
            public ConnectionProtocol Proto = 0;
            [XmlAttribute]
            public string PuttySession = null;
            [XmlAttribute]
            public string Username = null;
            [XmlAttribute]
            public string ExtraArgs = null;

            public static readonly ILog Log = LogManager.GetLogger(typeof(SessionData));

            public static SessionNode Import(string fileExport)
            {
                SessionNode root = new SessionNode("ImportedFromSuperPutty1040");

                try
                {
                    List<SessionData> sessions = new List<SessionData>();
                    if (File.Exists(fileExport))
                    {
                        XmlSerializer s = new XmlSerializer(sessions.GetType());
                        using (TextReader r = new StreamReader(fileExport))
                        {
                            sessions = (List<SessionData>)s.Deserialize(r);
                        }

                        foreach (SessionData oldSession in sessions)
                        {
                            List<string> folders = new List<string>(oldSession.SessionId.Split('/'));
                            folders.RemoveAt(folders.Count - 1);
                            SessionLeaf session = new SessionLeaf(oldSession.SessionName);
                            SessionNode parent = root.EnsureNodes(folders);
                            parent.AddChild(session);
                            session.ImageKey = oldSession.ImageKey;
                            session.Host = oldSession.Host;
                            session.Port = oldSession.Port;
                            session.Proto = oldSession.Proto;
                            session.PuttySession = oldSession.PuttySession;
                            session.Username = oldSession.Username;
                            session.ExtraArgs = oldSession.ExtraArgs;
                        }

                        Log.InfoFormat("Loaded {0} sessions from {1}", sessions.Count, fileExport);
                    }
                    else
                    {
                        Log.WarnFormat("Could not load sessions, file doesn't exist.  file={0}", fileExport);
                    }
                }
                catch (Exception ex)
                {
                    Log.WarnFormat("Could not fully import old sessions, msg={0}", ex.Message);
                }

                return root;
            }
        }
    }

    /// <summary>Helper methods used mostly for importing settings and session data from other applications</summary>
    public class PuttyDataHelper
    {
        public static readonly ILog Log = LogManager.GetLogger(typeof(PuttyDataHelper));

        public const string SessionDefaultSettings = "Default Settings";
        public const string SessionEmptySettings = "";

        public static RegistryKey RootAppKey
        {
            get
            {
                RegistryKey key = SuperPuTTY.IsKiTTY
                    ? Registry.CurrentUser.OpenSubKey(@"Software\9bis.com\KiTTY\Sessions")
                    : Registry.CurrentUser.OpenSubKey(@"Software\SimonTatham\PuTTY\Sessions");
                return key;
            }
        }
        public static List<string> GetSessionNames()
        {
            List<string> names = new List<string>();
            names.Add(SessionEmptySettings);
            RegistryKey key = RootAppKey;
            if (key != null)
            {
                string[] savedSessionNames = key.GetSubKeyNames();
                foreach (string rawSession in savedSessionNames)
                {
                    names.Add(HttpUtility.UrlDecode(rawSession));
                }
            }

            if (!names.Contains(SessionDefaultSettings))
            {
                names.Insert(1, SessionDefaultSettings);
            }

            return names;
        }

        public static SessionNode GetAllSessionsFromSuperPutty1040(string fileExport)
        {
            return Legacy1040.SessionData.Import(fileExport);
        }

        public static SessionNode GetAllSessionsFromSuperPutty1030()
        {
            SessionNode root = new SessionNode("ImportedFromSuperPutty1030");
            
            try
            {
                Log.Info("LoadSessionsFromRegistry...");
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Jim Radford\SuperPuTTY\Sessions");
                if (key != null)
                {
                    string[] sessionKeys = key.GetSubKeyNames();
                    foreach (string sessionKey in sessionKeys)
                    {
                        
                        SessionLeaf session = new SessionLeaf(sessionKey);
                        SessionNode parent = root;

                        RegistryKey itemKey = key.OpenSubKey(sessionKey);
                        if (itemKey != null)
                        {
                            string sessionId = (string)itemKey.GetValue("SessionId", sessionKey);
                            List<string> folders = new List<string>(sessionId.Split('/'));
                            folders.RemoveAt(folders.Count - 1);
                            parent = root.EnsureNodes(folders);
                            session.Host = (string)itemKey.GetValue("Host", "");
                            session.Port = (int)itemKey.GetValue("Port", 22);
                            session.Proto = (ConnectionProtocol)Enum.Parse(typeof(ConnectionProtocol), (string)itemKey.GetValue("Proto", "SSH"));
                            session.PuttySession = (string)itemKey.GetValue("PuttySession", "Default Session");
                            session.Username = (string)itemKey.GetValue("Login", "");
                            session.LastDockstate = (DockState)itemKey.GetValue("Last Dock", DockState.Document);
                        }

                        parent.AddChild(session);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Could not fully import old sessions, msg={0}", ex.Message);
            }

            return root;
        }

        public static SessionNode GetAllSessionsFromPuTTY()
        {
            SessionNode root = new SessionNode("ImportedFromPuTTY");

            RegistryKey key = RootAppKey;
            if (key != null)
            {
                string[] savedSessionNames = key.GetSubKeyNames();
                foreach (string keyName in savedSessionNames)
                {
                    RegistryKey sessionKey = key.OpenSubKey(keyName);
                    if (sessionKey != null)
                    {
                        SessionLeaf session = new SessionLeaf(HttpUtility.UrlDecode(keyName));
                        session.Host = (string)sessionKey.GetValue("HostName", "");
                        session.Port = (int)sessionKey.GetValue("PortNumber", 22);
                        session.Proto = (ConnectionProtocol)Enum.Parse(typeof(ConnectionProtocol), (string)sessionKey.GetValue("Protocol", "SSH"), true);
                        session.PuttySession = (string)sessionKey.GetValue("PuttySession", HttpUtility.UrlDecode(keyName));
                        session.Username = (string)sessionKey.GetValue("UserName", "");

                        if (String.IsNullOrEmpty(session.Host))
                        {
                            Log.WarnFormat("Could not fully import ({0}) as it has no host.", session.Name);
                            continue;
                        }

                        root.AddChild(session);
                    }
                }
            }

            return root;
        }

        public static SessionNode GetAllSessionsFromPuTTYCM(string fileExport)
        {
            SessionNode root = new SessionNode("ImportedFromPuTTYCM");

            if (fileExport == null || !File.Exists(fileExport))
            {
                return root;
            }

            XmlDocument doc = new XmlDocument();
            doc.Load(fileExport);

            XmlNodeList connections = doc.DocumentElement.SelectNodes("//connection[@type='PuTTY']");

            foreach (XmlElement connection in connections)
            {
                List<string> folders = new List<string>();
                XmlElement node = connection.ParentNode as XmlElement;
                while (node != null && node.Name != "root")
                {
                    if (node.Name == "container" && node.GetAttribute("type") == "folder")
                    {
                        folders.Add(node.GetAttribute("name"));
                    }
                    node = node.ParentNode as XmlElement;
                }
                folders.Reverse();
                SessionNode parent = root.EnsureNodes(folders);
                XmlElement info = (XmlElement)connection.SelectSingleNode("connection_info");
                XmlElement login = (XmlElement)connection.SelectSingleNode("login");

                SessionLeaf session = new SessionLeaf(info.SelectSingleNode("name").InnerText);
                session.Host = info.SelectSingleNode("host").InnerText;
                session.Port = Convert.ToInt32(info.SelectSingleNode("port").InnerText);
                session.Proto = (ConnectionProtocol)Enum.Parse(typeof(ConnectionProtocol), info.SelectSingleNode("protocol").InnerText);
                session.PuttySession = info.SelectSingleNode("session").InnerText;
                session.Username = login.SelectSingleNode("login").InnerText;
                parent.AddChild(session);
            }

            return root;
        }
    }
}
