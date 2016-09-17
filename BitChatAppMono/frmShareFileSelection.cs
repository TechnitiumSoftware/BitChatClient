/*
Technitium Bit Chat
Copyright (C) 2016  Shreyas Zare (shreyas@technitium.com)

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

using BitChatCore;
using BitChatCore.FileSharing;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace BitChatApp
{
    public partial class frmShareFileSelection : Form
    {
        List<BitChat> _selectedChats;

        public frmShareFileSelection(BitChatClient client, SharedFile sharedFile)
        {
            InitializeComponent();

            BitChat[] sharedChats = sharedFile.GetChatList();

            foreach (BitChat chat in client.GetBitChatList())
            {
                bool found = false;

                foreach (BitChat sharedChat in sharedChats)
                {
                    if (sharedChat == chat)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    checkedListBox1.Items.Add(chat);
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            _selectedChats = new List<BitChat>();

            foreach (BitChat item in checkedListBox1.CheckedItems)
                _selectedChats.Add(item);

            DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            this.Close();
        }

        public List<BitChat> SelectedChats
        { get { return _selectedChats; } }
    }
}
