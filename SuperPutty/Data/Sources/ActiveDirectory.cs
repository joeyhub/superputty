using System;
using System.Collections;
using System.Windows.Forms;
using System.Linq;
using System.Text;
using SuperPutty.Data;
using System.Xml.Serialization;
using System.DirectoryServices;
using System.Net;

namespace SuperPutty.Data.Sources
{
    #region Session Wrappers

    [XmlType("SessionActiveDirectorySource")]
    [XmlRoot("ActiveDirectory")]
    public class SessionActiveDirectorySource : SessionSource
    {
        private static NetworkBrowser browser = new NetworkBrowser();

        [XmlIgnore]
        public override bool IsReadOnly { get { return true; } }
   
        public SessionActiveDirectorySource()
            : base()
        {
            this.Guid = System.Guid.NewGuid().ToString();
        }

        // Note: Source is useless.
        public SessionActiveDirectorySource(string name, string source)
            : base(name, source)
        {
            this.Guid = System.Guid.NewGuid().ToString();
        }

        public override void Load()
        {
            DirectoryEntry root = new DirectoryEntry("WinNT:");

            foreach (DirectoryEntry obj in root.Children)
                foreach (DirectoryEntry computer in obj.Children)
                    if (computer.SchemaClassName.Equals("Computer"))
                    {
                        SessionWindowsNetworkLeaf session = new SessionWindowsNetworkLeaf(computer.Name);
                        session.Port = 22;
                        IPAddress[] ipAddresses = Dns.GetHostAddresses(computer.Name);

                        if (ipAddresses.Length > 0)
                            session.Host = ipAddresses[0].ToString();

                        this.AddChild(session);
                    }

            SessionSource.Register(this);
            this.OnLoaded();
        }

        public override void Save()
        {
        }
    }

    [XmlType("SessionActiveDirectoryLeaf")]
    [XmlRoot("Session")]
    public class SessionActiveDirectoryLeaf : SessionLeaf, IComparable, ICloneable
    {
        [XmlIgnore]
        public override bool IsReadOnly { get {return true;} }

        public SessionActiveDirectoryLeaf()
            : base()
        {
        }

        public SessionActiveDirectoryLeaf(string name)
            : base(name)
        {
        }

        public SessionActiveDirectoryLeaf(string name, string hostName, int port, ConnectionProtocol protocol, string sessionConfig)
            : base(name, hostName, port, protocol, sessionConfig)
        {
        }

        public override object Clone()
        {
            return this.PopulateClone(new SessionActiveDirectoryLeaf());
        }
    }

    #endregion
}
