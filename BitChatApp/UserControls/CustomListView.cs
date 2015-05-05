/*
Technitium Bit Chat
Copyright (C) 2015  Shreyas Zare (shreyas@technitium.com)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BitChatApp.UserControls
{
    public partial class CustomListView : UserControl
    {
        #region events

        public event EventHandler ItemClick;
        public event EventHandler ItemDoubleClick;
        public event MouseEventHandler ItemMouseUp;
        public event KeyEventHandler ItemKeyDown;
        public event KeyEventHandler ItemKeyUp;
        public event KeyPressEventHandler ItemKeyPress;

        #endregion

        #region variables

        const int BORDER_SIZE = 1;

        List<CustomListViewItem> _items = new List<CustomListViewItem>();

        CustomListViewItem _selectedItem;
        CustomListViewItem _lastScrolledItem;

        bool _sortItems = false;
        bool _autoScrollToBottom = false;

        int _borderPadding = 0;
        Color _borderColor = Color.Empty;

        #endregion

        #region constructor

        public CustomListView()
        {
            InitializeComponent();
        }

        #endregion

        #region private

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
                                         BorderColor, BORDER_SIZE, ButtonBorderStyle.Solid,
                                         BorderColor, BORDER_SIZE, ButtonBorderStyle.Solid,
                                         BorderColor, BORDER_SIZE, ButtonBorderStyle.Solid,
                                         BorderColor, BORDER_SIZE, ButtonBorderStyle.Solid);
        }

        protected override void OnResize(EventArgs e)
        {
            this.SuspendLayout();

            ReArrangeItems();

            base.OnResize(e);
            this.Refresh();

            this.ResumeLayout();
        }

        private bool OnSelectItem(CustomListViewItem item)
        {
            if (_selectedItem != item)
            {
                _selectedItem = item;

                foreach (CustomListViewItem control in _items)
                    control.Selected = control.Equals(item);

                return true;
            }

            return false;
        }

        private void ReArrangeItems()
        {
            if (_items.Count > 0)
            {
                //find total height of items including padding
                int totalHeightOfItems = 0;

                foreach (CustomListViewItem control in _items)
                {
                    totalHeightOfItems += control.Height + (_borderPadding * 2);
                }

                int requiredItemWidth;

                if (totalHeightOfItems > this.Height)
                {
                    //this means scrolling is enabled
                    requiredItemWidth = this.Width - _borderPadding * 2 - this.Padding.Left - this.Padding.Right - 17;
                }
                else
                {
                    //this means scrolling is disabled
                    requiredItemWidth = this.Width - _borderPadding * 2 - this.Padding.Left - this.Padding.Right;
                }

                //check width
                if (requiredItemWidth != _items[0].Width)
                {
                    for (int i = 0; i < _items.Count; i++)
                        _items[i].Width = requiredItemWidth;
                }

                //check item placement
                _items[0].Location = new Point(this.Padding.Left, this.Padding.Top + this.AutoScrollPosition.Y);

                for (int i = 1; i < _items.Count; i++)
                {
                    CustomListViewItem previousControl = _items[i - 1];
                    CustomListViewItem currentControl = _items[i];

                    currentControl.Location = new Point(previousControl.Location.X, previousControl.Location.Y + previousControl.Height + this.Padding.Bottom);
                }
            }
        }

        #region Item Events

        private void item_Clicked(object sender, EventArgs args)
        {
            this.SuspendLayout();

            OnSelectItem(sender as CustomListViewItem);

            this.ResumeLayout();

            if (ItemClick != null)
                ItemClick(this, EventArgs.Empty);
        }

        void item_DoubleClick(object sender, EventArgs e)
        {
            this.SuspendLayout();

            OnSelectItem(sender as CustomListViewItem);

            this.ResumeLayout();

            if (ItemDoubleClick != null)
                ItemDoubleClick(this, EventArgs.Empty);
        }

        void item_MouseUp(object sender, MouseEventArgs e)
        {
            CustomListViewItem obj = sender as CustomListViewItem;

            this.SuspendLayout();

            bool selected = OnSelectItem(obj);

            this.ResumeLayout();

            if (selected && (ItemClick != null))
                ItemClick(this, EventArgs.Empty);

            e = new MouseEventArgs(e.Button, e.Clicks, obj.Location.X + e.X, obj.Location.Y + e.Y, e.Delta);

            if (ItemMouseUp != null)
                ItemMouseUp(this, e);
        }

        void item_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (ItemKeyPress != null)
                ItemKeyPress(this, e);
        }

        void item_KeyUp(object sender, KeyEventArgs e)
        {
            if (ItemKeyUp != null)
                ItemKeyUp(this, e);
        }

        void item_KeyDown(object sender, KeyEventArgs e)
        {
            if (ItemKeyDown != null)
                ItemKeyDown(this, e);
        }

        void item_SortList(object sender, EventArgs e)
        {
            _items.Sort(delegate(CustomListViewItem item1, CustomListViewItem item2) { return string.Compare(item1.ToString(), item2.ToString()); });

            this.SuspendLayout();

            ReArrangeItems();

            this.ResumeLayout();
        }

        #endregion

        #endregion

        #region public

        public CustomListViewItem AddItem(CustomListViewItem item)
        {
            this.SuspendLayout();

            item.SeparatorColor = this.SeparatorColor;

            item.Click += item_Clicked;
            item.DoubleClick += item_DoubleClick;
            item.MouseUp += item_MouseUp;
            item.KeyPress += item_KeyPress;
            item.KeyUp += item_KeyUp;
            item.KeyDown += item_KeyDown;
            item.SortList += item_SortList;

            if (_items.Count > 0)
                item.Width = _items[_items.Count - 1].Width;

            _items.Add(item);
            this.Controls.Add(item);

            if (_sortItems)
                _items.Sort(delegate(CustomListViewItem item1, CustomListViewItem item2) { return string.Compare(item1.ToString(), item2.ToString()); });

            ReArrangeItems();

            this.ResumeLayout();

            if (_selectedItem == null)
            {
                _selectedItem = item;
                item.Selected = true;
            }

            if (_autoScrollToBottom)
                this.ScrollControlIntoView(item);

            return item;
        }

        public bool RemoveItem(CustomListViewItem item)
        {
            int index = _items.IndexOf(item);
            if (index > -1)
            {
                this.SuspendLayout();

                this.Controls.Remove(item);
                _items.Remove(item);

                ReArrangeItems();

                item.Click -= item_Clicked;
                item.DoubleClick -= item_DoubleClick;
                item.MouseUp -= item_MouseUp;
                item.KeyPress -= item_KeyPress;
                item.KeyUp -= item_KeyUp;
                item.KeyDown -= item_KeyDown;
                item.SortList -= item_SortList;

                if (_selectedItem == item)
                {
                    if (_items.Count > 0)
                    {
                        if (index > 0)
                            OnSelectItem(_items[index - 1]);
                        else
                            OnSelectItem(_items[index]);

                        if (ItemClick != null)
                            ItemClick(this, EventArgs.Empty);
                    }
                    else
                    {
                        _selectedItem = null;
                    }
                }

                this.ResumeLayout();

                return true;
            }

            return false;
        }

        public void RemoveAllItems()
        {
            this.Controls.Clear();
            _items.Clear();
        }

        public CustomListViewItem GetFirstItem()
        {
            if (_items.Count == 0)
                return null;

            return _items[0];
        }

        public CustomListViewItem GetLastItem()
        {
            if (_items.Count == 0)
                return null;

            return _items[_items.Count - 1];
        }

        public void SelectItem(CustomListViewItem item)
        {
            this.SuspendLayout();

            OnSelectItem(item);

            this.ResumeLayout();
        }

        public void ScrollToBottom()
        {
            if (_items.Count > 0)
            {
                _lastScrolledItem = _items[_items.Count - 1];
                this.ScrollControlIntoView(_lastScrolledItem);
            }
        }

        #endregion

        #region properties

        public Color BorderColor
        {
            get { return _borderColor; }
            set
            {
                _borderColor = value;

                if (_borderColor == Color.Empty)
                    _borderPadding = 0;
                else
                    _borderPadding = 1;
            }
        }

        public Color SeparatorColor
        { set; get; }

        public CustomListViewItem SelectedItem
        { get { return _selectedItem; } }

        public bool AutoScrollToBottom
        {
            get { return _autoScrollToBottom; }
            set { _autoScrollToBottom = value; }
        }

        public bool SortItems
        {
            get { return _sortItems; }
            set { _sortItems = value; }
        }

        #endregion
    }
}
