Download the zipped .exe here: [https://github.com/krassykrastev/FileStarter/releases](https://github.com/krassykrastev/FileStarter/releases)

04 Jun 2026

# 🚀 FileStarter


FileStarter is a lightweight Windows tray application written in C# with Visual Studio Code that automatically launches apps or files based on a configurable schedule. It is designed to be simple, reliable, and unobtrusive — perfect for automating your daily startup routine.

---

## ✨ Features:

- Launch up to 4 apps / files (Teams, Outlook, or custom files) on a configurable schedule
- Vacation mode (temporarily disable auto-start using date range) 
- Prevents launching duplicate instances
- Built-in retry mechanism and launch verification
- System tray integration with quick controls  
- Startup health check for missing custom files  
- Shows notifications for broken paths or other alerts 
- Start VPN first and only when it's connected then starts the selected file(s)
- Logs changes and other important events   
- Log file is automatically cleared at **1 MB** to prevent growth

---

## 🖥 Usage:

### Tray icon controls:
Right-click the tray icon to:

- Enable/disable Auto-start  
- Run FileStarter on Windows startup  
- Start VPN first  
- Open Settings  
- Open log  
- Help  
- About  
- Exit

### Quick actions:

- Single mouse click → toggle Auto-start ON/OFF  
- Double mouse click → open Settings  

---

## ⚙️ Settings:

From the Settings window you can:

- Configure daily schedules (Mon–Sun)  
- Set custom launch times per day  
- Select apps/files for each slot  
- Enable/disable each slot individually  
- Configure Vacation mode  

---

## 🏖 Vacation Mode: Temporarily disable auto-start using a date range:
Perfect for holidays / time off:
- Turn auto-start OFF from a specific date  
- Optionally set an "until" date  
- Auto-start resumes automatically after the period ends  

---

## 📂 Supported File Types:

- `.exe` applications  
- Documents (`.docx`, `.xlsx`, etc.)  
- Any file with a default Windows association  

---

## ⚙️ Installation:

1. Download the `.zip`
2. Extract it anywhere you like
3. Run `.exe` or place a shortcut on your desktop and run it from there.

Note: Windows may show a message that the app is not digitally signed. This is normal, as I don't have a code signing certificate (which requires an annual subscription), so I can't digitally sign the app. If this happens click on "More info -> Run anyway" 

---

## ❤️ Support this project:

- Enjoying FileStarter? [Buy me a beer ;-)](https://paypal.me/krasikrastev)
