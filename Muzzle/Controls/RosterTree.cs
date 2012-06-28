/* --------------------------------------------------------------------------
 * Copyrights
 *
 * Portions created by or assigned to Cursive Systems, Inc. are
 * Copyright (c) 2002-2008 Cursive Systems, Inc.  All Rights Reserved.  Contact
 * information for Cursive Systems, Inc. is available at
 * http://www.cursive.net/.
 *
 * License
 *
 * Jabber-Net is licensed under the LGPL.
 * See LICENSE.txt for details.
 * --------------------------------------------------------------------------*/
using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Windows.Forms;
using Bedrock.Collections;
using Jabber;
using Jabber.Client;
using Jabber.Protocol.Client;
using Jabber.Protocol.IQ;

namespace Muzzle.Controls
{
    /// <summary>
    /// A TreeView optimized for showing Jabber roster items.  Make sure that the
    /// form you drop this on has a JabberClient, a PresenceManager, and a RosterManager
    /// on the form first, and this widget will automatically connect to them.
    /// </summary>
    public partial class RosterTree : System.Windows.Forms.TreeView
    {
        // image list offsets
        private const int OFFLINE    = 0;
        private const int ONLINE     = 1;
        private const int AWAY       = 2;
        private const int XA         = 3;
        private const int DND        = 4;
        private const int CHATTY     = 5;
        private const int EXPANDED   = 6;
        private const int COLLAPSED  = 7;

        private const string UNFILED = "Unfiled";

        private Jabber.Client.RosterManager   m_roster = null;
        private Jabber.Client.PresenceManager m_pres = null;
        private Jabber.Client.JabberClient m_client = null;

        private IDictionary m_groups = new SkipList();
        private IDictionary m_items  = new SkipList();

        private Color m_statusColor = Color.Teal;

        /// <summary>
        /// Create a new RosterTree
        /// </summary>
        public RosterTree()
        {
            InitializeComponent();
        }

        private void RosterTree_AfterSelect(object sender, TreeViewEventArgs e)
        { }

        private void DrawGroup(DrawTreeNodeEventArgs e)
        {
            GroupNode node = (GroupNode)e.Node;
            string counts = String.Format("({0}/{1})", node.Current, node.Total);

            if (node.IsSelected)
            {
                string newText = node.GroupName + " " + counts;
                e.DrawDefault = true;
                if (node.Text != newText)
                    node.Text = newText;
                return;
            }
            Graphics g = e.Graphics;
            Brush fg = new SolidBrush(this.ForeColor);
            Brush stat_fg = new SolidBrush(this.StatusColor);

            g.DrawString(node.GroupName, this.Font, fg, new Point(e.Bounds.Left, e.Bounds.Top), StringFormat.GenericTypographic);
            if (node.Total > 0)
            {
                SizeF name_size = g.MeasureString(node.GroupName, this.Font);
                g.DrawString(counts, this.Font, stat_fg, new PointF(e.Bounds.Left + name_size.Width, e.Bounds.Top), StringFormat.GenericTypographic);
            }
        }

        private void DrawItem(DrawTreeNodeEventArgs e)
        {
            ItemNode node = (ItemNode)e.Node;
            if (node.IsSelected)
            {
                e.DrawDefault = true;
                return;
            }

            Graphics g = e.Graphics;
            Brush fg = new SolidBrush(this.ForeColor);
            Brush stat_fg = new SolidBrush(this.StatusColor);

            g.DrawString(node.Nickname, this.Font, fg, new Point(e.Bounds.Left, e.Bounds.Top), StringFormat.GenericTypographic);
            if (node.Status != null)
            {
                SizeF nick_size = g.MeasureString(node.Nickname, this.Font);
                g.DrawString("(" + node.Status + ")", this.Font, stat_fg, new PointF(e.Bounds.Left + nick_size.Width, e.Bounds.Top), StringFormat.GenericTypographic);
            }
        }


        private void RosterTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Node is GroupNode)
                DrawGroup(e);
            else if (e.Node is ItemNode)
                DrawItem(e);
            else
                e.DrawDefault = true; // or assert(false)
        }

        private GroupNode GetDropGroup(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(ItemNode)))
                return null;

            Point pt = this.PointToClient(new Point(e.X, e.Y));
            TreeNode node = this.GetNodeAt(pt);
            while (!(node is GroupNode) && (node != null))
            {
                node = node.Parent;
            }
            if (node == null)
                return null;

            ItemNode item = e.Data.GetData(typeof(ItemNode)) as ItemNode;
            if (item.Parent == node)
                return null;
            return (GroupNode)node;
        }

        private void RosterTree_DragDrop(object sender, DragEventArgs e)
        {
            GroupNode group = GetDropGroup(e);
            if (group == null)
                return;
            ItemNode item = e.Data.GetData(typeof(ItemNode)) as ItemNode;
            GroupNode parent = (GroupNode)item.Parent;
            Item i = (Item)item.Item.CloneNode(true, m_client.Document);
            i.RemoveGroup(parent.GroupName);
            i.AddGroup(group.GroupName);
            m_roster.Modify(i);
        }


        private void RosterTree_DragOver(object sender, DragEventArgs e)
        {
            if (GetDropGroup(e) == null)
                e.Effect = DragDropEffects.None;
            else
                e.Effect = DragDropEffects.Move;
        }


        private void RosterTree_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ItemNode)))
                e.Effect = DragDropEffects.Move;
            else
                e.Effect = DragDropEffects.None;
        }

        private void RosterTree_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (e.Item is ItemNode)
                this.DoDragDrop(e.Item, DragDropEffects.Move);
        }

        /// <summary>
        /// The RosterManager for this view
        /// </summary>
        [Category("Managers")]
        public RosterManager RosterManager
        {
            get
            {
                // If we are running in the designer, let's try to auto-hook a RosterManager
                if ((m_roster == null) && DesignMode)
                {
                    IDesignerHost host = (IDesignerHost)base.GetService(typeof(IDesignerHost));
                    this.RosterManager = (RosterManager)Jabber.Connection.StreamComponent.GetComponentFromHost(host, typeof(RosterManager));
                }
                return m_roster;
            }
            set
            {
                if ((object)m_roster == (object)value)
                    return;
                m_roster = value;
                if (m_roster != null)
                {
                    m_roster.OnRosterBegin += new Bedrock.ObjectHandler(m_roster_OnRosterBegin);
                    m_roster.OnRosterEnd += new Bedrock.ObjectHandler(m_roster_OnRosterEnd);
                    m_roster.OnRosterItem += new RosterItemHandler(m_roster_OnRosterItem);
                }
            }
        }

        /// <summary>
        /// The PresenceManager for this view
        /// </summary>
        [Category("Managers")]
        public PresenceManager PresenceManager
        {
            get
            {
                // If we are running in the designer, let's try to auto-hook a PresenceManager
                if ((m_pres == null) && DesignMode)
                {
                    IDesignerHost host = (IDesignerHost) base.GetService(typeof(IDesignerHost));
                    this.PresenceManager = (PresenceManager)Jabber.Connection.StreamComponent.GetComponentFromHost(host, typeof(PresenceManager));
                }
                return m_pres;
            }
            set
            {
                if ((object)m_pres == (object)value)
                    return;
                m_pres = value;
                if (m_pres != null)
                    m_pres.OnPrimarySessionChange += new PrimarySessionHandler(m_pres_OnPrimarySessionChange);
            }
        }

        /// <summary>
        /// The PresenceManager for this view
        /// </summary>
        [Category("Managers")]
        public JabberClient Client
        {
            get
            {
                // If we are running in the designer, let's try to auto-hook a JabberClient
                if ((m_client == null) && DesignMode)
                {
                    IDesignerHost host = (IDesignerHost) base.GetService(typeof(IDesignerHost));
                    this.Client = (JabberClient)Jabber.Connection.StreamComponent.GetComponentFromHost(host, typeof(JabberClient));
                }
                return m_client;
            }
            set
            {
                if ((object)m_client == (object)value)
                    return;
                m_client = value;
                if (m_client != null)
                    m_client.OnDisconnect += new Bedrock.ObjectHandler(m_client_OnDisconnect);
            }
        }

        /// <summary>
        /// Color to draw status text with.  Not applicable until .Net 2.0.
        /// </summary>
        [Category("Appearance")]
        public Color StatusColor
        {
            get { return m_statusColor; }
            set { m_statusColor = value; }
        }

        /// <summary>
        /// Should we draw status text next to each roster item?  Not applicable until .Net 2.0.
        /// </summary>
        [Category("Appearance")]
        [DefaultValue(true)]
        public bool DrawStatus
        {
            get
            {
                return (this.DrawMode == TreeViewDrawMode.OwnerDrawText);
            }
            set
            {
                if (value)
                    this.DrawMode = TreeViewDrawMode.OwnerDrawText;
                else
                    this.DrawMode = TreeViewDrawMode.Normal;
            }
        }

        /// <summary>
        /// The group names for the roster
        /// </summary>
        public string[] Groups
        {
            get
            {
                string[] g = new string[m_groups.Count];
                m_groups.Keys.CopyTo(g, 0);
                return g;
            }
        }

        /// <summary>
        /// Add a new, empty group, if this group doesn't exist, otherwise a no-op.
        /// </summary>
        /// <param name="groupName"></param>
        public TreeNode AddGroup(string groupName)
        {
            Group g = new Group(m_client.Document);
            g.GroupName = groupName;
            return AddGroupNode(g);
        }

        private void m_roster_OnRosterBegin(object sender)
        {
            this.BeginUpdate();
        }

        private void m_roster_OnRosterEnd(object sender)
        {
            this.EndUpdate();
        }

        /// <summary>
        /// After a group node is expanded, change to the down-triangle image.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnAfterExpand(TreeViewEventArgs e)
        {
            e.Node.ImageIndex = EXPANDED;
            e.Node.SelectedImageIndex = EXPANDED;

            base.OnAfterExpand (e);
        }

        /// <summary>
        /// After a group node is collapsed, change to the right-triangle image.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnAfterCollapse(TreeViewEventArgs e)
        {
            e.Node.ImageIndex = COLLAPSED;
            e.Node.SelectedImageIndex = COLLAPSED;

            base.OnAfterCollapse (e);
        }

        /// <summary>
        /// When mousing over a node, show a tooltip with the full JID.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ItemNode node = this.GetNodeAt(e.X, e.Y) as ItemNode;
            if (node == null)
            { // none selected, or a group
                tt.SetToolTip(this, "");
                return;
            }
            if (node.JID.ToString() != tt.GetToolTip(this))
            {
                tt.SetToolTip(this, node.JID.ToString());
            }
        }

        private GroupNode AddGroupNode(Group g)
        {
            GroupNode gn = (GroupNode)m_groups[g.GroupName];
            if (gn == null)
            {
                gn = new GroupNode(g);
                m_groups.Add(g.GroupName, gn);
                this.Nodes.Add(gn);
            }
            return gn;
        }

        private void m_roster_OnRosterItem(object sender, Jabber.Protocol.IQ.Item ri)
        {
            bool remove = (ri.Subscription == Subscription.remove);

            LinkedList nodelist = (LinkedList)m_items[ri.JID.ToString()];
            if (nodelist == null)
            {
                // First time through.
                if (!remove)
                {
                    nodelist = new LinkedList();
                    m_items.Add(ri.JID.ToString(), nodelist);
                }
            }
            else
            {
                // update to an existing item.  remove all of them, and start over.
                foreach (ItemNode i in nodelist)
                {
                    GroupNode gn = i.Parent as GroupNode;
                    i.Remove();
                    if ((gn != null) && (gn.Nodes.Count == 0))
                    {
                        m_groups.Remove(gn.GroupName);
                        gn.Remove();
                    }
                }
                nodelist.Clear();
                if (remove)
                    m_items.Remove(ri.JID.ToString());
            }

            if (remove)
                return;

            // add the new ones back
            Hashtable ghash = new Hashtable();
            Group[] groups = ri.GetGroups();
            for (int i=groups.Length-1; i>=0; i--)
            {
                if (groups[i].GroupName == "")
                    groups[i].GroupName = UNFILED;
            }

            if (groups.Length == 0)
            {
                groups = new Group[] { new Group(ri.OwnerDocument) };
                groups[0].GroupName = UNFILED;
            }

            foreach (Group g in groups)
            {
                GroupNode gn = AddGroupNode(g);
                // might have the same group twice.
                if (ghash.Contains(g.GroupName))
                    continue;
                ghash.Add(g.GroupName, g);

                ItemNode i = new ItemNode(ri);
                i.ChangePresence(m_pres[ri.JID]);
                nodelist.Add(i);
                gn.Nodes.Add(i);
            }
        }

        private void m_client_OnDisconnect(object sender)
        {
            this.Nodes.Clear();
            m_groups.Clear();
            m_items.Clear();
        }


        private void m_pres_OnPrimarySessionChange(object sender, JID bare)
        {
            Presence pres = m_pres[bare];
            LinkedList nodelist = (LinkedList) m_items[bare.ToString()];
            if (nodelist == null)
                return;

            foreach (ItemNode n in nodelist)
            {
                n.ChangePresence(pres);
            }
        }


        /// <summary>
        /// A TreeNode to hold a Roster Group
        /// </summary>
        public class GroupNode : TreeNode
        {
            private Jabber.Protocol.IQ.Group m_group;

            /// <summary>
            /// Create a GroupNode
            /// </summary>
            /// <param name="rg"></param>
            public GroupNode(Jabber.Protocol.IQ.Group rg) : base(rg.GroupName, COLLAPSED, COLLAPSED)
            {
                m_group = rg;
            }

            /// <summary>
            /// The name of the group
            /// </summary>
            public string GroupName
            {
                get { return m_group.GroupName; }
            }

            /// <summary>
            /// Total number of members of the group
            /// </summary>
            public int Total
            {
                // TODO: what if we're not showing offline?
                get { return this.Nodes.Count; }
            }

            /// <summary>
            /// Current number of online members of the group
            /// </summary>
            public int Current
            {
                get
                {
                    int count = 0;
                    foreach (ItemNode i in this.Nodes)
                    {
                        if (i.ImageIndex != OFFLINE)
                            count++;
                    }
                    return count;
                }
            }
        }

        /// <summary>
        /// A TreeNode to hold a RosterItem
        /// </summary>
        public class ItemNode : TreeNode
        {
            private Jabber.Protocol.IQ.Item m_item;
            private string m_status = null;
            private string m_nick = null;

            /// <summary>
            /// Create an ItemNode
            /// </summary>
            /// <param name="ri">The roster item to create from</param>
            public ItemNode(Jabber.Protocol.IQ.Item ri)
            {
                m_item = ri;
                m_nick = ri.Nickname;
                if ((m_nick == null) || (m_nick == ""))
                {
                    m_nick = ri.JID.User;
                    if ((m_nick == null) || (m_nick == ""))
                        m_nick = ri.JID.ToString(); // punt.
                }
                this.Text = m_nick;
            }

            /// <summary>
            /// The JID of this Roster Item
            /// </summary>
            public JID JID
            {
                get { return m_item.JID; }
            }

            /// <summary>
            /// Roster nickname for this user.
            /// </summary>
            public string Nickname
            {
                get { return m_nick; }
            }

            /// <summary>
            /// Last presence status for this item
            /// </summary>
            public string Status
            {
                get { return m_status; }
            }

            /// <summary>
            /// The roster item.  Please make a clone before using it.
            /// </summary>
            public Item Item
            {
                get { return m_item; }
            }

            /// <summary>
            /// Update this roster item with new presence information
            /// </summary>
            /// <param name="p"></param>
            public void ChangePresence(Presence p)
            {
                SelectedImageIndex = ImageIndex = getPresenceImage(p);

                string txt = null;
                if ((p == null) || (p.Status == null) || (p.Status == ""))
                {
                    txt = m_nick;
                    m_status = null;
                }
                else
                {
                    m_status = p.Status;
                    txt = m_nick + " (" + m_status + ")";
                }
                if (Text != txt)
                    Text = txt;
            }

            private static int getPresenceImage(Presence p)
            {
                if ((p == null) || (p.Type == PresenceType.unavailable))
                    return OFFLINE;

                switch (p.Show)
                {
                    case null:
                    case "":
                        return ONLINE;
                    case "away":
                        return AWAY;
                    case "xa":
                        return XA;
                    case "dnd":
                        return DND;
                    case "chat":
                        return CHATTY;
                }

                return OFFLINE;
            }
        }

    }
}
