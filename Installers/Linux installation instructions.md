# Direct Linux Install

- Connect to the Linux server with your favorite SSH client. I'm using Putty.
- You can get the IP address of the Linux machine via ```ip addr show```

- The Linux builds are self contained (they carry their own .NET runtime), so .NET does not need to be installed.
  Use the `x64` or the `arm64` zip to match the machine (`uname -m` says `x86_64` or `aarch64`).

- Install unzip
  > sudo apt install unzip

- Download CatMQ (be sure to change the URL for the version you want to install)
  > wget https://github.com/NTDLS/CatMQ/releases/download/2.3.0/CatMQ.linux.x64.zip
  >
  > (or `CatMQ.linux.arm64.zip` for ARM machines such as a Raspberry Pi or Graviton)

- Extract files
  > unzip CatMQ.linux.x64.zip -d CatMQ

- Make the File Executable
  > chmod +x ~/CatMQ/CatMQ.Service

- Execute the file
  > ~/CatMQ/CatMQ.Service

- Open CatMQ interface (if configured)
  - http://<your_ip_address>:45783/


# Windows to Linux file copy using SSH

- Linux
  - Get the IP address for the Linux machine
    > ip addr show

- Windows
  - Install putty, connect to SSH with ip address from previous step.

- Linux
  - Configure SSH for password auth for file copy.
    > sudo nano /etc/ssh/sshd_config

  - Look for the following lines and ensure they are set:
    - "PasswordAuthentication yes"

  - Restart the ssh service.
      > sudo systemctl restart ssh


- Linux
  - Install unzip
    > sudo apt install unzip

- Windows:
  - Copy package to the linux server.
    > scp .\output\CatMQ.linux.x64.zip josh@172.22.103.236:/home/josh

- Linux
  - Extract files
    > sudo unzip CatMQ.linux.x64.zip -d /opt/CatMQ

  - Make the File Executable
    > sudo chmod +x /opt/CatMQ/CatMQ.Service

  - Execute the file
    > sudo /opt/CatMQ/CatMQ.Service

- Windows
  - Browse to: http://172.22.103.236:45783/
