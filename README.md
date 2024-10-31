# UniKey

UniKey is a Windows background program that enhances your keyboard experience. You can define replacements (e.g. typos), convert text to Cyrillic, Greek, etc., HTML-escape text, etc.

## How to install

* Extract the zip file into any folder on your system.
* Right-click the executable and choose “Create shortcut”.
* Right-click the shortcut thusly created and choose “Properties”.
* In the “Shortcut” tab, click “Advanced”, then enable “Run as administrator”.
* Move that shortcut file to `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`. This will ensure UniKey runs at Windows startup.
* Double-click the shortcut file to run it now.

The first time UniKey is run, a message box will inform you that a settings file does not yet exist. Click “Create new file here”.

Afterwards, UniKey will be completely silent and invisible. It will listen to key presses, which means that you can type UniKey commands anywhere in any application, such as a text editor. The most important commands to remember are:

* `{help}` — displays a message box listing all of the commands UniKey knows.
* `{exit}` — exits UniKey, meaning it will no longer listen to keystrokes and no longer operate. Run the executable again to get it back.

## Repository hosting

At present, the git repository for this software is primarily hosted on GitHub, a proprietary platform. I try to keep a mirror on Codeberg up to date, but Codeberg does not have the workflow feature that automatically builds artifacts/releases for each commit. If such a feature exists by the time you read this, please submit a pull request adding the necessary workflow file(s) and then let me know directly.