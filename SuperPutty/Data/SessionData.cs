/*
 * Copyright (c) 2009 Jim Radford http://www.jimradford.com
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions: 
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using Microsoft.Win32;
using WeifenLuo.WinFormsUI.Docking;
using log4net;
using System.Xml.Serialization;
using System.IO;
using System.Collections;
using System.Reflection;
using System.Timers;

namespace SuperPutty.Data
{
    public enum ConnectionProtocol
    {
        SSH,
        SSH2,
        Telnet,
        Rlogin,
        Raw,
        Serial,
        Cygterm,
        Mintty
    }

    public enum ChangeType
    {
        Added, 
        Removed
    }

    /// <summary>
    /// This is a very lightweight class for storing a list of strings using a custom CSV style string.
    /// 
    /// This must be used on the command line so a string may not contain \0, \r, \n.
    /// 
    /// When using remote sources, take care of character sets which may create a security problem if a session is opened in a new instance.
    /// 
    /// The original intention was that a library with a common CSV protocol would be used.
    /// 
    /// This is not intended to write real CSV files and will not work with multiple line readers.
    /// 
    /// It is for concisely serialising a string representing a through a set of folders.
    /// 
    /// Although non-standard, the simplicity of this implementation has three key benefits.
    /// 
    /// 1. The legacy format is compatible with the reader for this format.
    /// 2. Implementing this another language is trivial.
    /// 3. It allows any character valid withint the character set to be used in a string.
    /// 
    /// If the user ensures that they never use the delimiter in their names, it will work with split and join.
    /// 
    /// Examples:
    /// {"a", "b", "c"} => a/b/c
    /// {"a/b", "c"} => a//b/c
    /// </summary>
    public class Csv
    {
        const char DELIMITER = '/';

        /// <summary>
        /// This method serializes a collection of strings into a CSV style string.
        /// 
        /// However, the default separator is a forward slash rather than a comma.
        /// 
        /// In the case of any error the response will be a null value.
        /// 
        /// The list passed must contain at least one item and no items can be null or empty.
        /// 
        /// If a string contains a delimiter it will be escaped with another delimiter.
        /// 
        /// An enquoted quote will be escaped with a quote.
        /// 
        /// No other characters are escaped including newlines.
        /// </summary>
        /// <param name="items">A list of strings.</param>
        /// <returns>null on error otherwise a string representing the list.</returns>
        public static string Write(ICollection<string> items)
        {
            if(items.Count == 0)
                return null;

            List<string> parts = new List<String>();
            string D = DELIMITER.ToString();

            foreach(string item in items)
            {
                if (item == null || item == "")
                    return null;

                if (item.Contains('\0') || item.Contains('\r') || item.Contains('\n'))
                    return null;

                parts.Add(item.Replace(D, D + D));
            }

            return String.Join(D, parts.ToArray());
        }

        /// <summary>
        /// This method deserializes a string returned from Write back into a string list.
        /// 
        /// In the case of any error null will be returned.
        /// 
        /// This implementation uses a very basic state machine like implementation,
        /// however it may also be implemented using regular expression with a look around.
        /// </summary>
        /// <param name="list">A string of one or more non-empty values separated by forward slash.</param>
        /// <returns>null when there is an error otherwise the derserialized list of strings.</returns>
        public static List<string> Read(string csv)
        {
            List<string> parts = new List<string>();
            string state = null;
            string current = "";

            foreach(char c in csv)
                switch(state)
                {
                    case null:
                    case "part":
                        if(c == DELIMITER)
                        {
                            state = "delimiter";
                            break;
                        }

                        state = "part";
                        current += c.ToString();
                        break;
                    case "delimiter":
                        state = "part";

                        if(c == DELIMITER)
                        {
                            current += c.ToString();
                            break;
                        }

                        if(current == "")
                            return null;

                        parts.Add(current);
                        current = c.ToString();
                        break;
                }

            switch(state)
            {
                case null:
                case "delimiter":
                    return null;
                case "part":
                    parts.Add(current);
                    break;
            }

            return parts;
        }
    }

    public class ChangedEventArgs : EventArgs
    {
        public SessionData Item { get; private set; }
        public ChangeType Type { get; private set; }

        public ChangedEventArgs(ChangeType type, SessionData item)
        {
            this.Type = type;
            this.Item = item;
        }
    }

    public delegate void ChangedEventHandler(ChangedEventArgs e);

    [XmlType("SessionChildren")]
    [XmlRoot("Children")]
    public class SessionChildren : KeyedCollection<string, SessionData>
    {
        public event ChangedEventHandler Changed;

        public SessionChildren() : base(null, 0)
        {
        }

        protected override string GetKeyForItem(SessionData item)
        {
            return item.Name;
        }

        protected override void InsertItem(int index, SessionData newItem)
        {
            base.InsertItem(index, newItem);

            if (this.Changed != null)
                this.Changed(new ChangedEventArgs(ChangeType.Added, newItem));
        }

        protected override void RemoveItem(int index)
        {
            SessionData removedItem = Items[index];
            base.RemoveItem(index);

            if (this.Changed != null)
                this.Changed(new ChangedEventArgs(ChangeType.Removed, removedItem));
        }
    }

    [XmlInclude(typeof(SessionNode)), XmlInclude(typeof(SessionLeaf))]
    public class SessionData
    {
        [XmlIgnore]
        public virtual bool IsReadOnly { get {return false;} }

        [XmlAttribute]
        public string Name;

        [XmlIgnore]
        public SessionNode Parent;

        public int CompareTo(object obj)
        {
            SessionData s = obj as SessionData;
            return s == null ? 1 : this.Name.CompareTo(s.Name);
        }

        public SessionData()
        {
        }

        public SessionData(string name)
        {
            Name = name;
        }

        public SessionSource GetSourceNode()
        {
            SessionData current = this.Parent;

            while (current != null)
            {
                if(current is SessionSource)
                    return (SessionSource)current;

                current = current.Parent;
            }

            return null;
        }

        public List<string> GetNames()
        {
            SessionData current = this;
            List<string> path = new List<string>();
            
            while(current != null)
            {
                path.Add(current.Name);
                current = current.Parent;
            }

            path.Reverse();
            return path;
        }

        public string GetNamesString()
        {
            return Csv.Write(this.GetNames());
        }

        public void Remove()
        {
            if(this.Parent != null)
                Parent.RemoveChild(this);
        }

        public object PopulateClone(object o)
        {
            foreach (FieldInfo pi in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (pi.Name == "Parent" || pi.Name == "Children")
                    continue;

                pi.SetValue(o, pi.GetValue(this));
            }

            return o;
        }

        public virtual object Clone()
        {
            return this.PopulateClone(new SessionData());
        }
    }

    [XmlInclude(typeof(SessionSource)), XmlInclude(typeof(SessionRoot))]
    [XmlType("SessionNode")]
    [XmlRoot("Folder")]
    public class SessionNode : SessionData, IComparable, ICloneable
    {
        [XmlArray("Children")]
        [XmlArrayItem(typeof(SessionNode), ElementName = "Folder")]
        [XmlArrayItem(typeof(SessionSource), ElementName = "Source")]
        [XmlArrayItem(typeof(SessionXmlFileSource), ElementName = "XmlFile")]
        [XmlArrayItem(typeof(SessionLeaf), ElementName = "Session")]
        public virtual SessionChildren Children { get; set; }

        private void Initialise()
        {
            this.Children = new SessionChildren();
        }

        public SessionNode() : base()
        {
            this.Initialise();
        }

        public SessionNode(string name) : base(name)
        {
            this.Initialise();
        }

        public virtual bool ShouldSerializeChildren()
        {
            return true;
        }

        public void AddChild(SessionData child)
        {
            child.Parent = this;
            this.Children.Add(child);
        }

        public void RemoveChild(SessionData child)
        {
            child.Parent = null;
            this.Children.Remove(child);

            if (child is SessionSource)
                SessionSource.Unregister((SessionSource)child);
        }

        public long GetCount()
        {
            long count = this.Children.Count;

            foreach(SessionData child in this.Children)
            {

                if (child is SessionNode)
                    count += ((SessionNode)child).GetCount();
            }

            return count;
        }

        public List<SessionData> GetChildren()
        {
            return new List<SessionData>(this.Children);
        }

        public List<T> Flatten<T>() where T : SessionData
        {
            List<T> children = new List<T>();

            foreach (SessionData child in this.Children)
            {
                if (child is T)
                    children.Add((T)child);

                if (child is SessionNode)
                    children.AddRange(((SessionNode)child).Flatten<T>());
            }

            return children;
        }

        public T GetByName<T>(string name) where T : SessionData
        {
            if(this.Children.Contains(name))
            {
                SessionData candidate = this.Children[name];

                if (candidate is T)
                    return (T)candidate;
            }

            return null;
        }

        public void SetParents()
        {
            foreach(SessionData child in this.Children)
            {
                child.Parent = this;

                if (child is SessionNode)
                    ((SessionNode)child).SetParents();
            }
        }

        public SessionNode EnsureNodes(IEnumerable<string> path)
        {
            SessionNode current = this;

            foreach(string name in path)
            {
                SessionNode next = current.GetByName<SessionNode>(name);

                if (next == null)
                {
                    next = new SessionNode(name);
                    current.AddChild(next);
                }

                current = next;
            }

            return current;
        }

        // Warning: Use this with duplicates at your own peril.
        public T GetByNames<T>(ICollection<string> node_names) where T : SessionData
        {
            SessionNode current = this;
            int i;

            for (i = 0; i < node_names.Count - 1; i++)
            {
                current = current.GetByName<SessionNode>(node_names.ElementAt<string>(i));

                if (current == null)
                    return null;
            }

            return (T)current.GetByName<T>(node_names.ElementAt<string>(i));
        }

        public T GetByNamesString<T>(string csv) where T : SessionData
        {
            List<string> names = Csv.Read(csv);

            if (names == null)
                return null;

            return this.GetByNames<T>(names);
        }

        public void OnChange(ChangedEventHandler e)
        {
            this.Children.Changed += e;
        }

        public void OffChange(ChangedEventHandler e)
        {
            this.Children.Changed -= e;
        }

        public override object Clone()
        {
            return this.PopulateClone(new SessionNode());
        }

        public SessionNode DeepClone()
        {
            SessionNode node = (SessionNode)this.Clone();

            foreach (SessionData child in this.Children)
                node.AddChild((SessionData)(child.Clone()));

            return node;
        }
    }

    [XmlType("SessionRoot")]
    [XmlRoot("Root")]
    public class SessionRoot : SessionNode
    {
        [XmlAttribute]
        public string Guid;

        public SessionRoot() : base()
        {
        }

        public SessionRoot(string name) : base(name)
        {
            this.Guid = System.Guid.NewGuid().ToString();
        }
    }

    [XmlInclude(typeof(SessionXmlFileSource))]
    [XmlType("SessionSource")]
    [XmlRoot("Source")]
    public abstract class SessionSource : SessionNode
    {
        public const string ROOT_NAME = "PuTTY Sessions";

        [XmlIgnore]
        public string Guid;

        // Note: Registry does not check for node existance.
        protected static Dictionary<string, SessionSource> Sources = new Dictionary<string, SessionSource>();

        public static void Register(SessionSource source)
        {
            if(!SessionSource.Sources.ContainsKey(source.Guid))
                SessionSource.Sources.Add(source.Guid, source);
        }

        public static void Unregister(SessionSource source)
        {
            // Note: This means the source has not been loaded, or something worse.
            if (source.Guid == null)
                return;

            SessionSource.Sources.Remove(source.Guid);
        }

        public static bool IsRegistered(SessionSource source)
        {
            // Warning: This is deceptive. This permits reloading.
            return SessionSource.Sources.ContainsKey(source.Guid) && SessionSource.Sources[source.Guid] != source;
        }

        [XmlAttribute]
        public string Source;
        public event EventHandler Loaded;

        public SessionSource() : base()
        {
        }

        /// <summary>
        /// This constructor is to be used when the user creates a new source node from a writable source.
        /// It will create a GUID to prevent circular references.
        /// </summary>
        /// <param name="name">The name of the node for display purposes.</param>
        /// <param name="source">The source. The inheriting class should make use of this as the location of its data source.</param>
        public SessionSource(string name, string source) : base(name)
        {
            this.Guid = System.Guid.NewGuid().ToString();
            this.Source = source;
        }

        public override bool ShouldSerializeChildren()
        {
            return false;
        }

        public void OnLoaded()
        {
            if (this.Loaded != null)
                this.Loaded(this, null);
        }

        public abstract void Save();
        public abstract void Load();
    }

    [XmlType("SessionXmlFileSource")]
    [XmlRoot("XmlFile")]
    public class SessionXmlFileSource : SessionSource
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionXmlFileSource));

        public SessionXmlFileSource() : base()
        {
        }

        public SessionXmlFileSource(string name, string source) : base(name, source)
        {
        }

        public override void Load()
        {
            SessionRoot root = LoadSessionsFromFile(this.Source, this.Name);

            if (root == null)
                return;

            this.Guid = root.Guid;

            if(SessionSource.IsRegistered(this))
            {
                this.Remove();
                Log.WarnFormat("Removed {0} due to circular reference.", this.Guid);
                return;
            }

            this.Children = root.Children;

            foreach (SessionData child in this.Children)
                child.Parent = this;

            SessionSource.Register(this);
            this.OnLoaded();
        }

        private bool IsDirty = false;

        public override void Save()
        {
            if (this.IsDirty)
                return;

            if (this.WaitAfterSave != null)
            {
                Log.InfoFormat("Delaying save for {0}", this.Source);
                this.IsDirty = true;
                return;
            }

            Log.InfoFormat("Saving {0}", this.Source);
            this.RealSave(this.Source);
            this.WaitAfterSave = new Timer(10000);
            this.WaitAfterSave.AutoReset = false;
            this.WaitAfterSave.Elapsed += new ElapsedEventHandler(this.WaitedAfterSave);
            this.WaitAfterSave.Start();
        }

        public void Save(string fileName)
        {
            this.RealSave(fileName);
        }

        private Timer WaitAfterSave;

        private void WaitedAfterSave(object sender, EventArgs e)
        {
            if (this.IsDirty)
            {
                Log.InfoFormat("Delayed save for {0}", this.Source);
                this.RealSave(this.Source);
            }


            this.WaitAfterSave.Elapsed -= new ElapsedEventHandler(this.WaitedAfterSave);
            this.WaitAfterSave = null;
        }

        private void RealSave(string fileName)
        {
            SessionRoot root = new SessionRoot(this.Name);
            root.Children = this.Children;
            root.Guid = this.Guid;
            SaveSessionsToFile(root, fileName);
            this.IsDirty = false;
        }

        public static SessionRoot LoadSessionsFromFile(string fileName, string name)
        {
            SessionRoot root = null;
            SessionRoot empty = new SessionRoot(name);

            if (File.Exists(fileName))
            {
                XmlSerializer s = new XmlSerializer(empty.GetType());
                using (TextReader r = new StreamReader(fileName))
                {
                    root = (SessionRoot)s.Deserialize(r);
                }
                Log.InfoFormat("Loaded sessions from {0}", fileName);
            }
            else
            {
                Log.WarnFormat("Could not load sessions, file doesn't exist.  file={0}", fileName);
            }

            if (root == null)
                root = empty;

            root.SetParents();

            return root;
        }

        /// <summary>Save session configuration to the specified XML file</summary>
        /// <param name="sessions">A <seealso cref="List"/> containing the session configuration data</param>
        /// <param name="fileName">A path to a filename to save the data in</param>
        public static void SaveSessionsToFile(SessionNode root, string fileName)
        {
            Log.InfoFormat("Saving sessions to {0}", fileName);
            BackUpFiles(fileName, 20);
            XmlSerializer s = new XmlSerializer(root.GetType());
            using (TextWriter w = new StreamWriter(fileName))
            {
                s.Serialize(w, root);
            }
        }

        private static void BackUpFiles(string fileName, int count)
        {
            if (File.Exists(fileName) && count > 0)
            {
                try
                {
                    // backup
                    string fileBaseName = Path.GetFileNameWithoutExtension(fileName);
                    string dirName = Path.GetDirectoryName(fileName);
                    string backupName = Path.Combine(dirName, string.Format("{0}.{1:yyyyMMdd_hhmmss}.XML", fileBaseName, DateTime.Now));
                    File.Copy(fileName, backupName, true);

                    // limit last count saves
                    List<string> oldFiles = new List<string>(Directory.GetFiles(dirName, fileBaseName + ".*.XML"));
                    oldFiles.Sort();
                    oldFiles.Reverse();
                    if (oldFiles.Count > count)
                    {
                        for (int i = 20; i < oldFiles.Count; i++)
                        {
                            Log.InfoFormat("Cleaning up old file, {0}", oldFiles[i]);
                            File.Delete(oldFiles[i]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Error backing up files", ex);
                }
            }
        }
    }

    [XmlType("SessionLeaf")]
    [XmlRoot("Session")]
    public class SessionLeaf : SessionData, IComparable, ICloneable
    {
        [XmlAttribute]
        public string ImageKey;
        [XmlAttribute]
        public string Host;
        [XmlAttribute]
        public int Port;
        [XmlAttribute]
        public ConnectionProtocol Proto;
        [XmlAttribute]
        public string PuttySession;
        [XmlAttribute]
        public string Username;
        [XmlAttribute]
        public string ExtraArgs;

        [XmlIgnore]
        public DockState LastDockstate = DockState.Document;
        [XmlIgnore]
        public bool AutoStartSession = false;
        [XmlIgnore]
        public string Password;

        public SessionLeaf() : base()
        {
        }

        public SessionLeaf(string name) : base(name)
        { 
        }

        public SessionLeaf(string name, string hostName, int port, ConnectionProtocol protocol, string sessionConfig) : base(name)
        {
            Host = hostName;
            Port = port;
            Proto = protocol;
            PuttySession = sessionConfig;
        }

        public override string ToString()
        {
            if (this.Proto == ConnectionProtocol.Cygterm || this.Proto == ConnectionProtocol.Mintty)
            {
                return string.Format("{0}://{1}", this.Proto.ToString().ToLower(), this.Host);
            }
            else
            {
                return string.Format("{0}://{1}:{2}", this.Proto.ToString().ToLower(), this.Host, this.Port);
            }
        }

        public override object Clone()
        {
            return this.PopulateClone(new SessionLeaf());
        }
    }
}
