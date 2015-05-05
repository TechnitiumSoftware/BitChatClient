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
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace BitChatApp.UserControls
{
    public partial class ChatMessageInfoItem : BitChatApp.UserControls.CustomListViewItem
    {
        const int BORDER_SIZE = 1;

        Color BorderColor = Color.FromArgb(224, 224, 223);
        DateTime _date;

        public ChatMessageInfoItem()
        {
            InitializeComponent();
        }

        public ChatMessageInfoItem(string message)
        {
            InitializeComponent();

            label1.Text = message;
            label2.Visible = false;
        }

        public ChatMessageInfoItem(string message, DateTime date)
        {
            InitializeComponent();

            _date = date;

            label1.Text = message;
            label2.Text = date.ToShortTimeString();
        }

        public bool IsDateSet()
        {
            return label2.Visible;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
                                         BorderColor, 0, ButtonBorderStyle.Solid,
                                         BorderColor, BORDER_SIZE, ButtonBorderStyle.Solid,
                                         BorderColor, 0, ButtonBorderStyle.Solid,
                                         BorderColor, BORDER_SIZE, ButtonBorderStyle.Solid);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            this.Refresh();
        }

        public DateTime MessageDate
        { get { return _date; } }
    }
}
