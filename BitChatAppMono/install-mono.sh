#!/bin/bash


/usr/bin/clear
login_name=`/usr/bin/whoami`
date=`/bin/date`
echo -e "\n\tHELLO $login_name !\tToday is $date"
echo -e "\n\t\e[00;31m==============================================================\e[00m"
echo -e "\tMAKE SURE YOU ARE RUNNING THIS SCRIPT AS THE \e[00;34mROOT USER\e[00m"
echo -e "\tIF NOT THEN EXIT NOW AND TRY AGAIN"
echo -e "\t\e[00;31m==============================================================\e[00m"

echo -e "\t"
read -p "       Press 'Y' to continue or any other key to exit  : " ans

if [ "${ans}" = "Y" -o "${ans}" = "y" ]
then

apt-get -y install mono-complete
mozroots --import --ask-remove


echo "%sudo ALL = NOPASSWD: /usr/bin/mono" >>/etc/sudoers

mkdir -p /opt/
cd ..
mv BitChat/ /opt/
chmod  755 /opt/BitChat
cd ~

#--------------makes the menu entry--------------
cat >/usr/share/applications/bitchat.desktop <<eof
[Desktop Entry]
Version=2.0
Type=Application
Name=BitChat
Icon=/opt/BitChat/bitchat_logo.png
Exec=sh -c "sudo /usr/bin/mono /opt/BitChat/BitChat.exe" "&"
Comment=BitChat is a secure, peer-to-peer instant messenger, which can be used in public as private networks
Categories=Network;InstantMessaging;
Terminal=false
eof
#--------------makes the menu entry--------------

else

echo -e "\n\t\e[00;31m==============================================================\e[00m"
echo -e "\tInstallation failed Plese execute install.sh as \e[00;34mROOT USER\e[00m"
echo -e "\t\e[00;31m==============================================================\e[00m"

fi
