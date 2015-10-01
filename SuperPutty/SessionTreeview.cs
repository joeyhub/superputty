﻿/*
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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using log4net;
using SuperPutty.Data;
using SuperPutty.Utils;
using WeifenLuo.WinFormsUI.Docking;
using SuperPutty.Gui;
using System.IO;
using System.Text.RegularExpressions;

namespace SuperPutty
{
    public partial class SessionTreeview : ToolWindow, IComparer
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SessionTreeview));

        private static int MaxSessionsToOpen = Convert.ToInt32(ConfigurationManager.AppSettings["SuperPuTTY.MaxSessionsToOpen"] ?? "10");

        public const string ImageKeySession = "computer";
        public const string ImageKeyFolder = "folder";

        TreeNodeFactory treeNodeFactory;
        SourceTreeNode nodeRoot;

        /// <summary>
        /// Instantiate the treeview containing the sessions
        /// </summary>
        /// <param name="dockPanel">The DockPanel container</param>
        /// <remarks>Having the dockpanel container is necessary to allow us to
        /// dock any terminal or file transfer sessions from within the treeview class</remarks>
        public SessionTreeview()
        {
            InitializeComponent();

            this.treeView1.TreeViewNodeSorter = this;
            
            if (SuperPuTTY.Images != null)
            {
                this.treeView1.ImageList = SuperPuTTY.Images;
            }
            
            this.treeNodeFactory = new TreeNodeFactory(this.treeView1.ImageList, this.contextMenuStripFolder, this.contextMenuStripAddTreeItem);
            this.ApplySettings();
            this.treeView1.BeginUpdate();
            this.LoadSessions();
            this.treeView1.EndUpdate();
            SuperPuTTY.Settings.SettingsSaving += new SettingsSavingEventHandler(Settings_SettingsSaving);
            SuperPuTTY.SessionsLoaded += new EventHandler(this.LoadedSessions);
        }

        void ExpandInitialTree()
        {
            if (SuperPuTTY.Settings.ExpandSessionsTreeOnStartup)
            {
                nodeRoot.ExpandAll();
                this.treeView1.SelectedNode = nodeRoot;
            }
            else
            {
                // start with semi-collapsed view
                nodeRoot.Expand();
                foreach (TreeNode node in this.nodeRoot.Nodes)
                    if (node is FolderTreeNode)
                        node.Collapse();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
        }

        void Settings_SettingsSaving(object sender, CancelEventArgs e)
        {
            this.ApplySettings();
        }

        void ApplySettings()
        {
            if (SuperPuTTY.Settings.OpenSessionWith != null)
            {
                // Note: I hope this garbage collects the events appropriately.
                this.toolStripMenuItem4.DropDownItems.Clear();

                foreach (KeyValuePair<string, Properties.Setting.OpenWith> entry in SuperPuTTY.Settings.OpenSessionWith)
                {
                    ToolStripMenuItem menuItem = new ToolStripMenuItem();
                    menuItem.Text = entry.Key;
                    menuItem.Click += new EventHandler(this.OpenWithToolStripMenuItem_Click);
                    this.toolStripMenuItem4.DropDownItems.Add(menuItem);
                }
            }

            this.treeView1.ShowLines = SuperPuTTY.Settings.SessionsTreeShowLines;
            this.treeView1.Font = SuperPuTTY.Settings.SessionsTreeFont;
            this.panelSearch.Visible = SuperPuTTY.Settings.SessionsShowSearch;
        }

        protected override void OnClosed(EventArgs e)
        {
            SuperPuTTY.Settings.SettingsSaving -= new SettingsSavingEventHandler(Settings_SettingsSaving);
            base.OnClosed(e);
        }

        /// <summary>
        /// Load the sessions from the registry and populate the treeview control
        /// </summary>
        void LoadSessions()
        {
            this.treeView1.Nodes.Clear();
            this.nodeRoot = this.treeNodeFactory.createSource(SuperPuTTY.Sessions);
            treeView1.Nodes.Add(this.nodeRoot);
            this.nodeRoot.ContextMenuStrip = this.contextMenuStripFolder;
            ExpandInitialTree();
        }

        private void LoadedSessions(object sender, EventArgs e)
        {
            this.LoadSessions();
        }

        /// <summary>
        /// Opens the selected session when the node is double clicked in the treeview
        /// </summary>
        /// <param name="sender">The treeview control that was double clicked</param>
        /// <param name="e">An Empty EventArgs object</param>
        private void treeView1_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            // e is null if this method is called from connectToolStripMenuItem_Click
            TreeNode rawNode = e != null ? e.Node : treeView1.SelectedNode;

            if (!(rawNode is SessionTreeNode))
                return;

            SessionTreeNode node = (SessionTreeNode)rawNode;

            if (node == treeView1.SelectedNode)
            {
                SuperPuTTY.OpenPuttySession(node.Session);
            }
        }

        private void OpenWithToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionLeaf session = ((SessionTreeNode)treeView1.SelectedNode).Session;
            ToolStripMenuItem menuItem = (ToolStripMenuItem)sender;
            Properties.Setting.OpenWith openWith = SuperPuTTY.Settings.OpenSessionWith[menuItem.Text];
            string args = openWith.Args.Replace("{Host}", session.Host);
            Process.Start(openWith.Process, args);
            Log.InfoFormat("Process.start {0} {1}", openWith.Process, args);
        }

        /// <summary>
        /// Create/Update a session entry
        /// </summary>
        /// <param name="sender">The toolstripmenuitem control that was clicked</param>
        /// <param name="e">An Empty EventArgs object</param>
        private void CreateOrEditSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!(sender is ToolStripMenuItem))
                return;

            SessionLeaf session = null;
            SessionLeaf source = null;
            FolderTreeNode target = this.nodeRoot;
            string title = null;
            DataTreeNode node = (DataTreeNode)treeView1.SelectedNode;

            ToolStripMenuItem menuItem = (ToolStripMenuItem)sender;
            bool isFolderNode = node is FolderTreeNode;

            if (menuItem.Text.ToLower().Equals("new") || isFolderNode)
            {
                target = (FolderTreeNode)(isFolderNode ? treeView1.SelectedNode : treeView1.SelectedNode.Parent);
                title = "Create New Session";
            }
            else if (menuItem == this.createLikeToolStripMenuItem)
            {
                // copy as
                source = ((SessionTreeNode)node).Session;
                session = (SessionLeaf)source.Clone();
                target = (FolderTreeNode)treeView1.SelectedNode.Parent;
                title = "Create New Session Like " + session.Name;
            }
            else
            {
                // edit, session node selected
                source = ((SessionTreeNode)node).Session;
                session = source;
                target = (FolderTreeNode)node.Parent;
                title = "Edit Session: " + session.Name;
            }

            dlgEditSession form = new dlgEditSession(session, this.treeView1.ImageList);
            form.Text = title;
            form.SessionNameValidator += delegate(string txt, out string error)
            {
                error = String.Empty;
                return true;
            };
            
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                SessionNode parent = ((FolderTreeNode)target).Session;

                if (source != null)
                    session.Remove();

                parent.AddChild(form.Session);
                form.Session.GetSourceNode().Save();
            }
            
        }

        /// <summary>
        /// Forces a node to be selected when right clicked, this assures the context menu will be operating
        /// on the proper session entry.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView1.SelectedNode = treeView1.GetNodeAt(e.X, e.Y);
            }          
        }

        /// <summary>
        /// Delete a session entry from the treeview and the registry
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DataTreeNode node = (DataTreeNode)treeView1.SelectedNode;
            SessionData session = node.Session;

            DialogResult result = MessageBox.Show(
                "Are you sure you want to delete " + session.Name + "?",
                "Delete Session?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                node.Remove();
                SessionSource source = session.GetSourceNode();
                session.Remove();
                source.Save();
            }
        }

        /// <summary>
        /// Open a directory listing on the selected nodes host to allow dropping files
        /// for drag + drop copy.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void fileBrowserToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SessionLeaf session = ((SessionTreeNode)treeView1.SelectedNode).Session;
            SuperPuTTY.OpenScpSession(session);
        }

        /// <summary>
        /// Shortcut for double clicking an entries node.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void connectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            treeView1_NodeMouseDoubleClick(null, null);
        }

        /// <summary>
        /// Open putty with args but as external process
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void connectExternalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node is SessionTreeNode)
            {
                PuttyStartInfo startInfo = new PuttyStartInfo(((SessionTreeNode)node).Session);
                startInfo.StartStandalone();
            }
        }

        private void connectInNewSuperPuTTYToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node is SessionTreeNode)
            {
                SuperPuTTY.LoadSessionInNewInstance(((SessionTreeNode)node).Session);
            }
        }

        private void newNormalFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null && node is FolderTreeNode)
            {
                dlgRenameItem dialog = new dlgRenameItem();
                dialog.Text = "New Folder";
                dialog.ItemName = "New Folder";
                dialog.DetailName = "";
                dialog.ItemNameValidator = delegate(string txt, out string error)
                {
                    error = String.Empty;
                    return true;
                };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SessionNode session = new SessionNode(dialog.ItemName);
                    (((FolderTreeNode)node).Session).AddChild(session);
                    session.GetSourceNode().Save();
                }
            }
        }

        private void copyFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FolderTreeNode node = (FolderTreeNode)this.treeView1.SelectedNode;
            if (node != null)
            {
                SessionNode session = node.Session.DeepClone();
                dlgRenameItem dialog = new dlgRenameItem();
                dialog.Text = "Copy Folder As";
                dialog.ItemName = session.Name;
                dialog.DetailName = "";
                dialog.ItemNameValidator = delegate(string txt, out string error)
                {
                    error = String.Empty;
                    return true;
                };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    session.Name = dialog.ItemName;
                    SessionNode parent = ((FolderTreeNode)node.Parent).Session;
                    parent.AddChild(session);
                    session.GetSourceNode().Save();
                }
            }
        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FolderTreeNode node = (FolderTreeNode)this.treeView1.SelectedNode;
            if (node != null)
            {
                SessionNode session = node.Session;
                dlgRenameItem dialog = new dlgRenameItem();
                dialog.Text = "Rename Folder";
                dialog.ItemName = session.Name;
                dialog.DetailName = "";
                dialog.ItemNameValidator = delegate(string txt, out string error)
                {
                    error = String.Empty;
                    return true;
                };
                if (dialog.ShowDialog(this) == DialogResult.OK && node.Text != dialog.ItemName)
                {
                    node.Text = dialog.ItemName;
                    session.Name = dialog.ItemName;
                    session.GetSourceNode().Save();
                }
            }
        }

        private void reloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SourceTreeNode node = (SourceTreeNode)this.treeView1.SelectedNode;
            node.Session.Load();
        }

        private void removeFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FolderTreeNode node = (FolderTreeNode)this.treeView1.SelectedNode;
            if (node != null && node != this.nodeRoot)
            {
                DialogResult result = MessageBox.Show(
                    "Remove Folder [" + node.Text + "]?",
                    "Remove Folder?", 
                    MessageBoxButtons.YesNo);

                if (result != DialogResult.Yes)
                    return;

                SessionSource source = node.Session.GetSourceNode();
                node.Session.Remove();
                source.Save();
                SuperPuTTY.ReportStatus("Removed Folder, {0}", node.Text);
            }
        }

        private void connectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null && node is FolderTreeNode)
            {
                List<SessionLeaf> sessions = ((FolderTreeNode)node).FlattenTags<SessionLeaf>();
                Log.InfoFormat("Found {0} sessions", sessions.Count);

                if (sessions.Count > MaxSessionsToOpen)
                {
                    DialogResult result = MessageBox.Show(
                        "Open All " + sessions.Count + " sessions?", 
                        "WARNING", 
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

                    if (result == DialogResult.Cancel)
                    {
                        // bug out...too many sessions to open
                        return;
                    }
                }

                foreach (SessionLeaf session in sessions)
                    SuperPuTTY.OpenPuttySession(session);
            }
        }

        private void contextMenuStripFolder_Opening(object sender, CancelEventArgs e)
        {
            DataTreeNode node = (DataTreeNode)this.treeView1.SelectedNode;
            bool isRootNode = this.treeView1.SelectedNode == this.nodeRoot;
            this.reloadToolStripMenuItem.Enabled = false;
            bool isSourceNode = this.treeView1.SelectedNode is SourceTreeNode;
            bool isReadOnly = node.Session.IsReadOnly;

            if (isSourceNode)
            {
                SessionSource session = ((SourceTreeNode)node).Session;

                if(session.Guid != null)
                    this.reloadToolStripMenuItem.Text = "Load";
                else
                    this.reloadToolStripMenuItem.Text = "Reload";

                this.reloadToolStripMenuItem.Enabled = true;
            }

            this.newActiveDirectoryFolderToolStripMenuItem.Enabled = !isReadOnly;
            this.renameToolStripMenuItem.Enabled = !(isRootNode || isReadOnly);
            this.copyFolderToolStripMenuItem.Enabled = !(isRootNode || isSourceNode || isReadOnly);
            this.removeFolderToolStripMenuItem.Enabled = !(isRootNode || isReadOnly);
        }

        private void contextMenuStripAddTreeItem_Opening(object sender, CancelEventArgs e)
        {
            // disable file transfers if pscp isn't configured.
            fileBrowserToolStripMenuItem.Enabled = SuperPuTTY.IsScpEnabled;

            connectInNewSuperPuTTYToolStripMenuItem.Enabled = !SuperPuTTY.Settings.SingleInstanceMode;
        }

        #region Node helpers

        TreeNode AddFolderNode(TreeNode parentNode, String nodeName)
        {
            TreeNode nodeNew = null;
            if (parentNode.Nodes.ContainsKey(nodeName))
            {
                SuperPuTTY.ReportStatus("Node with the same name exists.  New node ({0}) NOT added", nodeName);
            }
            else
            {
                SuperPuTTY.ReportStatus("Adding new folder, {1}.  parent={0}", parentNode.Text, nodeName);
                nodeNew = parentNode.Nodes.Add(nodeName, nodeName, ImageKeyFolder, ImageKeyFolder);
                nodeNew.ContextMenuStrip = this.contextMenuStripFolder;
            }
            return nodeNew;
        }

        public int Compare(object x, object y)
        {
            TreeNode tx = x as TreeNode;
            TreeNode ty = y as TreeNode;

            return string.Compare(tx.Text, ty.Text);
        }

        private void expandAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
              TreeNode node = this.treeView1.SelectedNode;
              if (node != null)
              {
                  this.treeView1.Parent.Hide();
                  node.ExpandAll();
                  this.treeView1.Parent.Show();
              }
        }

        private void collapseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode node = this.treeView1.SelectedNode;
            if (node != null)
            {
                node.Collapse();

                if (node == this.nodeRoot)
                {
                    nodeRoot.Expand();
                }
            }
        }

        #endregion

        #region Drag Drop

        private void treeView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            // Get the tree
            TreeView tree = (TreeView)sender;

            // Get the node underneath the mouse.
            DataTreeNode node = e.Item as DataTreeNode;

            // Start the drag-and-drop operation with a cloned copy of the node.
            if (node != null && node != this.nodeRoot && !node.Session.IsReadOnly)
            {
                this.treeView1.DoDragDrop(new TreeNodeDroppabble(node), DragDropEffects.Copy);
            }
        }

        private void treeView1_DragOver(object sender, DragEventArgs e)
        {
            // Get the tree.
            TreeView tree = (TreeView)sender;

            // Drag and drop denied by default.
            e.Effect = DragDropEffects.None;

            // Is it a valid format?
            TreeNodeDroppabble droppable = (TreeNodeDroppabble)e.Data.GetData(typeof(TreeNodeDroppabble));

            if (droppable != null)
            {
                DataTreeNode nodePayload = droppable.Node;
                // Get the screen point.
                Point pt = new Point(e.X, e.Y);

                // Convert to a point in the TreeView's coordinate system.
                pt = tree.PointToClient(pt);

                TreeNode node = tree.GetNodeAt(pt);

                if (node == null)
                    return;

                tree.SelectedNode = node;

                while(true)
                {
                    if (node == nodePayload)
                        return;

                    node = node.Parent;

                    if (node == null)
                        break;
                }

                e.Effect = DragDropEffects.Copy;
            }
        }

        private void treeView1_DragDrop(object sender, DragEventArgs e)
        {
            Log.DebugFormat("Drag drop");

            TreeView tree = (TreeView)sender;

            TreeNodeDroppabble droppable = (TreeNodeDroppabble)e.Data.GetData(typeof(TreeNodeDroppabble));
            DataTreeNode payload = droppable.Node;
            SessionData session = ((DataTreeNode)payload).Session;

            Point pt = new Point(e.X, e.Y);
            pt = tree.PointToClient(pt);
            TreeNode target = tree.GetNodeAt(pt);
            
            if (target is SessionTreeNode)
                target = target.Parent;

            SessionNode parent = ((FolderTreeNode)target).Session;
            SessionSource source1 = session.GetSourceNode();
            session.Remove();
            parent.AddChild(session);
            SessionSource source2 = session.GetSourceNode();

            source1.Save();

            if (source1 != source2)
                source2.Save();

            target.Expand();
        }

        #endregion

        #region Icon
        bool IsValidImage(string imageKey)
        {
            bool valid = false;
            if (!string.IsNullOrEmpty(imageKey))
            {
                valid = this.treeView1.ImageList.Images.ContainsKey(imageKey);
                if (!valid)
                {
                    Log.WarnFormat("Missing icon, {0}", imageKey);
                }
            }
            return valid;
        }


        #endregion

        #region Search
        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    this.ApplySearch(this.txtSearch.Text);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    this.ClearSearch();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
            }
        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            this.ApplySearch(this.txtSearch.Text);
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            this.ClearSearch();
        }

        private void ClearSearch()
        {
            this.txtSearch.Text = "";
            this.ApplySearch("");
        }

        private void ApplySearch(string txt)
        {
            Log.InfoFormat("Applying Search: txt={0}.", txt);
            this.treeView1.Parent.Hide();
            this.treeView1.BeginUpdate();

            bool isClear = string.IsNullOrEmpty(txt);

            if(isClear)
                this.treeNodeFactory.filter = null;
            else
                this.treeNodeFactory.filter = new SearchFilter(SuperPuTTY.Settings.SessionsSearchMode, txt);
            
            // reload
            this.LoadSessions();

            // if "clear" show make sure behaviour is consistent with reloading the list.
            if (isClear)
                this.ExpandInitialTree();
            else
                this.treeView1.ExpandAll();


            this.treeView1.EndUpdate();
            this.treeView1.Parent.Show();
        }

        public enum SearchMode
        {
            CaseSensitive, CaseInSensitive, Regex
        }

        public class SearchFilter
        {
            public SearchFilter(string mode, string filter)
            {
                this.Mode = FormUtils.SafeParseEnum(mode, true, SearchMode.CaseSensitive);
                this.Filter = filter;
                if (this.Mode == SearchMode.Regex)
                {
                    try
                    {
                        this.Regex = new Regex(filter);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Could not parse pattern: " + filter, ex);
                    }
                }
            }
            public bool IsMatch(SessionData s)
            {
                if (this.Mode == SearchMode.CaseInSensitive)
                {
                    return s.Name.ToLower().Contains(this.Filter.ToLower());
                }
                else if (this.Mode == SearchMode.Regex)
                {
                    return this.Regex != null ? this.Regex.IsMatch(s.Name) : true;
                }
                else
                {
                    // case sensitive
                    return s.Name.Contains(this.Filter);
                }
            }
            public SearchMode Mode { get; set; }
            public string Filter { get; set; }
            public Regex Regex { get; set; }
        }
        #endregion

        #region Key Handling
        private void treeView1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13 && this.treeView1.SelectedNode is SessionTreeNode)
            {
                if (Control.ModifierKeys == Keys.None)
                {
                    treeView1_NodeMouseDoubleClick(null, null);
                    e.Handled = true;
                }
                else if (Control.ModifierKeys == Keys.Shift)
                {
                    CreateOrEditSessionToolStripMenuItem_Click(this.settingsToolStripMenuItem, e);
                    e.Handled = true;
                }
            }
        }
        #endregion

        private void toolStripMenuItem4_Click(object sender, EventArgs e)
        {

        }

        public class TreeNodeFactory
        {
            private ImageList Images;
            private ContextMenuStrip AddTreeFolder;
            private ContextMenuStrip AddTreeSession;
            public SearchFilter filter;

            public TreeNodeFactory(ImageList images, ContextMenuStrip addTreeFolder, ContextMenuStrip addTreeSession)
            {
                this.Images = images;
                this.AddTreeFolder = addTreeFolder;
                this.AddTreeSession = addTreeSession;
            }

            public DataTreeNode create(SessionData session)
            {
                if (session is SessionSource)
                    return this.createSource((SessionSource)session);

                if (session is SessionNode)
                    return this.createFolder((SessionNode)session);

                if (session is SessionLeaf)
                    return this.createSession((SessionLeaf)session);

                return null;
            }

            public SourceTreeNode createSource(SessionSource session)
            {
                SourceTreeNode treeNode = new SourceTreeNode(session, this, this.AddTreeFolder, this.filter);
                return treeNode;
            }

            public FolderTreeNode createFolder(SessionNode session)
            {
                FolderTreeNode treeNode = new FolderTreeNode(session, this, this.AddTreeFolder, this.filter);
                return treeNode;
            }

            public SessionTreeNode createSession(SessionLeaf session)
            {
                SessionTreeNode treeNode = new SessionTreeNode(session, this.Images, this, this.AddTreeSession, this.filter);
                return treeNode;
            }

            public TreeNodeFactory Copy()
            {
                return new TreeNodeFactory(this.Images, this.AddTreeFolder, this.AddTreeSession);
            }
        }

        public class DataTreeNode : TreeNode
        {
            protected TreeNodeFactory Factory;
            public bool Show = true;
            protected SessionData _Session;
            public SessionData Session { get { return this._Session; } protected set { this._Session = value; } }

            public DataTreeNode(SessionData session, TreeNodeFactory factory, ContextMenuStrip menu, SearchFilter filter)
            {
                this._Session = session;
                this.Text = session.Name;
                this.Factory = factory;
                this.ContextMenuStrip = menu;
                this.Show = filter == null;
                this.Update(filter);
            }

            protected void Update(SearchFilter filter)
            {
                if (!this.Show)
                {
                    this.Show = filter.IsMatch(this.Session);

                    if (this.Show)
                        this.Factory = this.Factory.Copy();
                }
            }
        }

        public class FolderTreeNode : DataTreeNode
        {
            public new SessionNode Session { get { return (SessionNode)this._Session; } protected set { this._Session = value; } }
            private Dictionary<string, DataTreeNode> Unsorted = new Dictionary<string, DataTreeNode>();

            public FolderTreeNode(SessionNode session, TreeNodeFactory factory, ContextMenuStrip menu, SearchFilter filter)
                : base(session, factory, menu, filter)
            {
                this.ToolTipText = String.Format("Path: {0}", session.GetNamesString());
                this.ImageKey = SessionTreeview.ImageKeyFolder;
                this.SelectedImageKey = SessionTreeview.ImageKeyFolder;
                this.Update(filter);
                session.OnChange(new ChangedEventHandler(Sessions_ListChanged));
            }

            protected new void Update(SearchFilter filter)
            {
                base.Update(filter);
                this.Nodes.Clear();

                foreach (SessionData child in this.Session.GetChildren())
                {
                    DataTreeNode node = this.Factory.create(child);

                    if (node.Show)
                        this.Nodes.Add(node);

                    this.Unsorted.Add(child.Name, node);
                }

                this.Show = this.Show || this.Nodes.Count > 0;
            }

            ~FolderTreeNode()
            {
                this.Session.OffChange(new ChangedEventHandler(Sessions_ListChanged));
            }

            public List<T> FlattenTags<T>() where T : SessionData
            {
                List<T> children = new List<T>();

                foreach (DataTreeNode node in this.Nodes)
                {
                    SessionData child = (SessionData)node.Session;

                    if (child is T)
                        children.Add((T)child);

                    if (node is FolderTreeNode)
                        children.AddRange(((FolderTreeNode)node).FlattenTags<T>());
                }

                return children;
            }

            protected void Sessions_ListChanged(ChangedEventArgs e)
            {
                DataTreeNode node;

                switch (e.Type)
                {
                    case ChangeType.Added:
                        node = this.Factory.create(e.Item);

                        if (node.Show)
                            this.Nodes.Add(node);

                        this.Unsorted.Add(e.Item.Name, node);
                        break;
                    case ChangeType.Removed:
                        node = this.Unsorted[e.Item.Name];
                        this.Unsorted.Remove(e.Item.Name);

                        if (node.Show)
                            node.Remove();

                        break;
                }
            }
        }

        public class SourceTreeNode : FolderTreeNode
        {
            public new SessionSource Session { get { return (SessionSource)this._Session; } protected set { this._Session = value; } }

            public SourceTreeNode(SessionSource session, TreeNodeFactory factory, ContextMenuStrip menu, SearchFilter filter)
                : base(session, factory, menu, filter)
            {
                session.Loaded += new EventHandler(this.OnLoaded);
            }

            ~SourceTreeNode()
            {
                this.Session.Loaded -= new EventHandler(this.OnLoaded);
            }

            private void OnLoaded(object sender, EventArgs e)
            {
                this.TreeView.BeginUpdate();
                this.Session.OffChange(new ChangedEventHandler(this.Sessions_ListChanged));
                base.Update(this.Factory.filter);
                this.Session.OnChange(new ChangedEventHandler(this.Sessions_ListChanged));
                this.TreeView.EndUpdate();
            }
        }

        public class SessionTreeNode : DataTreeNode
        {
            public new SessionLeaf Session { get { return (SessionLeaf)this._Session; } protected set { this._Session = value; } }

            public SessionTreeNode(SessionLeaf session, ImageList images, TreeNodeFactory factory, ContextMenuStrip menu, SearchFilter filter)
                : base(session, factory, menu, filter)
            {
                this.Text = session.Name;
                this.ToolTipText = session.ToString();
                this.Show = filter == null || filter.IsMatch(session);
                string imageKey = SessionTreeview.ImageKeySession;

                if (!String.IsNullOrEmpty(session.ImageKey) && images.Images.ContainsKey(session.ImageKey))
                    imageKey = session.ImageKey;

                this.ImageKey = imageKey;
                this.SelectedImageKey = imageKey;
            }
        }

        public class TreeNodeDroppabble
        {
            public DataTreeNode Node { get; private set; }

            public TreeNodeDroppabble(DataTreeNode node)
            {
                this.Node = node;
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {

        }

        private void SessionTreeview_Load(object sender, EventArgs e)
        {

        }
    }
}
