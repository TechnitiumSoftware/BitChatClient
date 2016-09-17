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
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BitChatApp
{
    public partial class frmViewGroup : Form
    {
        #region variables

        BitChat _chat;

        Image _groupImage;
        bool _changesMade = false;

        #endregion

        #region constructor

        public frmViewGroup(BitChat chat)
        {
            InitializeComponent();

            _chat = chat;

            //name
            string name = _chat.NetworkName;

            //name icon
            if (name.Length > 0)
            {
                labIcon.Text = name.Substring(0, 1).ToUpper();

                int x = name.LastIndexOf(" ", StringComparison.CurrentCultureIgnoreCase);
                if (x > 0)
                {
                    labIcon.Text += name.Substring(x + 1, 1).ToUpper();
                }
                else if (name.Length > 1)
                {
                    labIcon.Text += name.Substring(1, 1).ToLower();
                }
            }
            else
            {
                labIcon.Text = "";
            }

            labIcon.BackColor = Color.FromArgb(102, 153, 255);
            labName.Text = name;

            //image icon
            if (_chat.GroupImage != null)
            {
                using (MemoryStream mS = new MemoryStream(_chat.GroupImage))
                {
                    _groupImage = Image.FromStream(mS);
                    picIcon.Image = _groupImage;
                }

                picIcon.Visible = true;
                labIcon.Visible = false;
            }
        }

        #endregion

        #region form code

        private void mnuChangePhoto_Click(object sender, EventArgs e)
        {
            using (frmImageDialog frm = new frmImageDialog())
            {
                if (frm.ShowDialog(this) == DialogResult.OK)
                {
                    _groupImage = frm.SelectedImage;
                    picIcon.Image = _groupImage;

                    using (MemoryStream mS = new MemoryStream(4096))
                    {
                        frm.SelectedImage.Save(mS, System.Drawing.Imaging.ImageFormat.Jpeg);
                        _chat.GroupImage = mS.ToArray();
                    }

                    _changesMade = true;

                    picIcon.Visible = true;
                    labIcon.Visible = false;
                }
            }
        }

        private void mnuRemovePhoto_Click(object sender, EventArgs e)
        {
            _chat.GroupImage = null;
            _groupImage = null;
            picIcon.Image = Properties.Resources.change_photo;

            _changesMade = true;

            picIcon.Visible = false;
            labIcon.Visible = true;
        }

        private void labIcon_MouseUp(object sender, MouseEventArgs e)
        {
            switch (e.Button)
            {
                case MouseButtons.Left:
                    mnuChangePhoto_Click(null, null);
                    break;

                case MouseButtons.Right:
                    mnuRemovePhoto.Enabled = (_groupImage != null);
                    Control control = sender as Control;
                    mnuGroupImage.Show(control, e.Location);
                    break;
            }
        }

        private void labIcon_MouseEnter(object sender, EventArgs e)
        {
            if (_groupImage == null)
            {
                picIcon.Visible = true;
                labIcon.Visible = false;
            }
            else
            {
                picIcon.Image = Properties.Resources.change_photo;
            }
        }

        private void picIcon_MouseEnter(object sender, EventArgs e)
        {
            if (_groupImage != null)
                picIcon.Image = Properties.Resources.change_photo;
        }

        private void picIcon_MouseLeave(object sender, EventArgs e)
        {
            if (_groupImage == null)
            {
                labIcon.Visible = true;
                picIcon.Visible = false;
            }
            else
            {
                picIcon.Image = _groupImage;
            }
        }

        private void mnuCopy_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.Clear();
                Clipboard.SetText(labName.Text);
            }
            catch
            { }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            if (_changesMade)
                this.DialogResult = DialogResult.OK;

            this.Close();
        }

        #endregion
    }
}
