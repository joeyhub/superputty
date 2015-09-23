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

    public class SessionCollection
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionCollection));
        public const string ROOT_NAME = "PuTTY Sessions";

        public SessionNode root {get; private set;}
        public event EventHandler RootLoaded;
        private string FileName;
        
        public long Count
        {
            get { return this.root.GetCount(); }
            set {}
        }

        public SessionCollection(string fileName)
        {
            this.root = new SessionNode(SessionCollection.ROOT_NAME);
            this.FileName = fileName;
        }

        private void Load()
        {
            this.root = SessionStorage.LoadSessionsFromFile(this.FileName);

            if (this.RootLoaded != null)
                this.RootLoaded(this, null);
        }

        public void Reload()
        {
            this.Load();
        }

        /// <summary>Saves the sessions to file (see constructor).</summary>
        public void Save()
        {
            SessionStorage.SaveSessionsToFile(this.root, this.FileName);
        }

        /// <summary>Import sessions from a from a <seealso cref="List"/> object into the specified folder</summary>
        /// <param name="sessions">A <seealso cref="List"/> of <seealso cref="SessionData"/> objects</param>
        /// <param name="folder">The destination folder name</param>
        public void Import(SessionNode root)
        {
            this.root.AddChild(root);
            this.Save();
        }
    }

    public class SessionStorage
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionStorage));

        public static SessionNode LoadSessionsFromFile(string fileName)
        {
            SessionNode root = null;
            SessionNode empty = new SessionNode(SessionCollection.ROOT_NAME);

            if (File.Exists(fileName))
            {
                XmlSerializer s = new XmlSerializer(empty.GetType());
                using (TextReader r = new StreamReader(fileName))
                {
                    root = (SessionNode)s.Deserialize(r);
                }
                Log.InfoFormat("Loaded sessions from {1}", fileName);
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

    [XmlInclude(typeof(SessionNode)), XmlInclude(typeof(SessionLeaf))]
    public class SessionData
    {
        public static string GetUniqueId()
        {
            return DateTime.Now.Ticks.ToString();
        }

        public static bool IsStringIdValid(string id)
        {
            return new Regex("^(([0-9]+)([.][0-9]+)*)?_[0-9]+$").IsMatch(id);
        }

        [XmlAttribute]
        public string Name;

        [XmlAttribute]
        public int Id;
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

        public List<int> GetIds()
        {
            SessionData current = this.Parent;
            List<int> path = new List<int>();

            while (current != null)
            {
                path.Add(current.Id);
                current = current.Parent;
            }

            path.Reverse();
            return path;
        }

        public string GetIdString()
        {
            string[] ids = Array.ConvertAll<int, string>(this.GetIds().ToArray(), x => x.ToString());
            return String.Join(".", ids) + '_' + this.Id.ToString();
        }

        public List<string> GetFullPath()
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

        public string GetFullPathToString()
        {
            return String.Join(" -> ", this.GetFullPath().ToArray());
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
                if (pi.Name == "Parent" || pi.Name == "Id" || pi.Name == "Children")
                    continue;

                pi.SetValue(o, pi.GetValue(this));
            }

            return o;
        }

        public object Clone()
        {
            return this.PopulateClone(new SessionData());
        }
    }

    [XmlType("SessionNode")]
    [XmlRoot("Folder")]
    public class SessionNode : SessionData, IComparable, ICloneable
    {
        [XmlArrayItem(typeof(SessionNode), ElementName = "Folder")]
        [XmlArrayItem(typeof(SessionLeaf), ElementName = "Session")]
        public BindingList<SessionData> Children = new BindingList<SessionData>();
        [XmlAttribute]
        public int Increment = 0;

        public SessionNode() : base()
        {
        }

        public SessionNode(string name) : base(name)
        {
        }

        public void AddChild(SessionData child)
        {
            child.Id = this.Increment++;
            child.Parent = this;
            this.Children.Add(child);
        }

        public void RemoveChild(SessionData child)
        {
            child.Parent = null;
            this.Children.Remove(child);
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

        public T GetFirstByName<T>(string name) where T : SessionData
        {
            foreach (SessionData child in this.Children)
            {
                if (child is T && child.Name.Equals(name))
                    return (T)child;
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
                SessionNode next = current.GetFirstByName<SessionNode>(name);

                if (next == null)
                {
                    next = new SessionNode(name);
                    current.AddChild(next);
                }

                current = next;
            }

            return current;
        }

        public T GetById<T>(int id) where T : SessionData
        {
            foreach (SessionData child in this.Children)
            {
                if (child is T && child.Id == id)
                    return (T)child;
            }

            return null;
        }

        // Warning: Use this with duplicates at your own peril.
        public SessionLeaf GetByNames(ICollection<string> node_names, string leaf_name)
        {
            SessionNode current = this;

            foreach (string node_name in node_names)
            {
                current = current.GetFirstByName<SessionNode>(node_name);

                if (current == null)
                    return null;
            }

            return (SessionLeaf)current.GetFirstByName<SessionLeaf>(leaf_name);
        }

        // Things like this could be made faster with an index.
        public SessionLeaf GetByIds(ICollection<int> node_ids, int leaf_id)
        {
            SessionNode current = this;
            bool first = true;

            if (this.Id != node_ids.ElementAt<int>(0))
                return null;

            foreach (int node_id in node_ids)
            {
                if(first)
                {
                    first = false;
                    continue;
                }

                current = current.GetById<SessionNode>(node_id);

                if (current == null)
                    return null;
            }

            return (SessionLeaf)current.GetById<SessionLeaf>(leaf_id);
        }

        public SessionLeaf GetByStringId(string id)
        {
            string[] parts = id.Split('_');
            int[] ids = Array.ConvertAll<string, int>(parts[0].Split('.'), Int32.Parse);
            return this.GetByIds(ids, Int32.Parse(parts[1]));
        }

        public SessionLeaf GetByLegacyId(string id)
        {
            List<string> parts = new List<string>(id.Split('/'));
            string name = parts[parts.Count - 1];
            parts.RemoveAt(parts.Count - 1);
            return this.GetByNames(parts, name);
        }

        public void OnChange(ListChangedEventHandler e)
        {
            this.Children.ListChanged += e;
        }

        public void OffChange(ListChangedEventHandler e)
        {
            this.Children.ListChanged -= e;
        }

        public new object Clone()
        {
            return this.PopulateClone(new SessionNode());
        }

        public SessionNode DeepClone()
        {
            SessionNode node = (SessionNode)this.Clone();

            foreach (SessionData child in this.Children)
                node.AddChild(child);

            return node;
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

        public new object Clone()
        {
            return this.PopulateClone(new SessionLeaf());
        }
    }
}
