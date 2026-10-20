# Publishing Hard Hat Media Player on GitHub

A step-by-step guide for the maintainer. Commands are for **PowerShell** in the project folder
(`C:\Users\…\VLC Player`). Replace `YOUR-USERNAME` with your GitHub username.

---

## 1. Create a GitHub account

Sign up at <https://github.com/signup> (free). Then turn on two-factor authentication:
**Settings → Password and authentication**.

## 2. Keep your personal email private

Git stamps every commit with an email address, and on GitHub that address becomes public.

1. On GitHub: **Settings → Emails** → tick **Keep my email addresses private**. GitHub shows you a private
   address like `12345678+YOUR-USERNAME@users.noreply.github.com`. Copy it.
2. Tick **Block command line pushes that expose my email** on the same page.
3. Tell Git to use your name and that address for this project:

   ```powershell
   git config user.name "Your Name"
   git config user.email "12345678+YOUR-USERNAME@users.noreply.github.com"
   ```

4. The commits made so far carry your Gmail address. Rewrite them to use the private address
   (safe now, because nothing has been published yet):

   ```powershell
   git rebase --root --exec "git commit --amend --no-edit --reset-author"
   git log --format="%h %an <%ae>  %s"
   ```

   Every line of the log should now show the noreply address.

## 3. Name the main branch `main`

GitHub's default branch name is `main`, and the build check in `.github/workflows/build.yml` runs on it:

```powershell
git branch -M main
```

## 4. Create the repository on GitHub

1. Go to <https://github.com/new>.
2. **Repository name:** `hard-hat-media-player`
3. **Description:** `A free, open-source media player for Windows built on libVLC.`
4. Choose **Public**.
5. Leave **Add a README**, **.gitignore** and **license** unticked; the project already has them.
6. Click **Create repository**.

## 5. Upload (push) the code

```powershell
git remote add origin https://github.com/YOUR-USERNAME/hard-hat-media-player.git
git push -u origin main
```

The first push opens a browser window to sign in to GitHub. Refresh the repository page afterwards: you
should see the README, and GitHub detects the license as **GPL-3.0**.

## 6. Set up the repository page

On the repository page, click the gear next to **About**:

- **Website:** `https://hardhatmediaplayer.com`
- **Topics:** `media-player`, `video-player`, `libvlc`, `wpf`, `windows`, `dotnet`, `open-source`

Then **Settings → General**:

- **Features:** keep **Issues** on; turn on **Discussions** for questions and ideas.

And **Settings → Branches → Add branch ruleset** (or **Add rule**) for `main`:

- **Require a pull request before merging**
- **Require status checks to pass** → choose **build**

This way every contribution is built automatically before it can be merged.

## 7. Publish a first release (a download people can run)

Build a ready-to-run folder. It includes .NET, so users don't need to install anything, and only the
64-bit libVLC:

```powershell
dotnet publish src/HardHatMediaPlayer -c Release -r win-x64 --self-contained true -o publish/HardHatMediaPlayer
Copy-Item LICENSE, THIRD-PARTY-NOTICES.md publish/HardHatMediaPlayer
Compress-Archive publish/HardHatMediaPlayer HardHatMediaPlayer-0.1.0-win-x64.zip
```

On GitHub: **Releases → Draft a new release**

- **Tag:** `v0.1.0` (create new tag on `main`)
- **Title:** `Hard Hat Media Player 0.1.0`
- Describe what's in it, attach `HardHatMediaPlayer-0.1.0-win-x64.zip`, then **Publish release**.

The zip isn't digitally signed, so Windows SmartScreen may warn people on first run ("More info → Run
anyway"). Code signing can come later.

## 8. Put the website on hardhatmediaplayer.com (your own server)

The website is the `site/` folder: plain HTML, CSS and an SVG icon, with no build step, no database and no PHP.
Any web server can host it. You publish it by hand: change the files, package them, upload them.

**First**, replace every `YOUR-USERNAME` in `site/index.html` with your GitHub username. To preview the site,
open `site/index.html` in a browser.

### a. Find out what kind of server you have

Look in the welcome email or the account page of the company you rent the server from:

- **Hosting with a control panel** (cPanel, Plesk, DirectAdmin, or your host's own panel): you log in to a
  website and see a **File Manager**. Follow **b1**.
- **A VPS or dedicated server** (Linux, you log in with `ssh`): follow **b2**.

If you aren't sure, ask the hosting company's support: "Is this shared hosting with a control panel, or a VPS
with SSH access? What is the server's IP address?"

### b1. Hosting with a control panel

1. Package the site from the repository folder:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\package-site.ps1
   ```

   This creates `dist\hardhatmediaplayer-site.zip`.
2. In the panel, add `hardhatmediaplayer.com` as a domain (cPanel: **Domains → Create a new domain**; often
   already there if you bought the domain from the same company). Note its **document root**, usually
   `public_html` or `public_html/hardhatmediaplayer.com`.
3. Open **File Manager**, go to that folder, delete the placeholder `index.html` the host put there (if any),
   **Upload** the zip, then right-click it → **Extract**, and delete the zip.
   `index.html` must end up directly in the document root, not inside a subfolder.
4. HTTPS: in cPanel, **SSL/TLS Status → Run AutoSSL**; in Plesk, **SSL/TLS Certificates → Let's Encrypt**.
   Most hosts do this automatically once the domain points to them (step c).

### b2. A Linux VPS (Ubuntu or Debian)

Replace `USER` with your login and `SERVER-IP` with the server's IP address.

1. Install a web server once. **Caddy** is the simplest because it gets and renews the HTTPS certificate on its
   own (install steps: caddyserver.com/docs/install). Then create the site folder:

   ```bash
   sudo mkdir -p /var/www/hardhatmediaplayer
   sudo chown USER /var/www/hardhatmediaplayer
   ```

2. Put this in `/etc/caddy/Caddyfile` and run `sudo systemctl reload caddy`:

   ```
   hardhatmediaplayer.com {
       root * /var/www/hardhatmediaplayer
       file_server
       encode gzip
   }

   www.hardhatmediaplayer.com {
       redir https://hardhatmediaplayer.com{uri} permanent
   }
   ```

   Already using **nginx**? Add a server block instead, then get the certificate with
   `sudo certbot --nginx -d hardhatmediaplayer.com -d www.hardhatmediaplayer.com`:

   ```nginx
   server {
       listen 80;
       server_name hardhatmediaplayer.com www.hardhatmediaplayer.com;
       root /var/www/hardhatmediaplayer;
       index index.html;
   }
   ```

3. Upload the site from the repository folder on your PC (Windows 10/11 include `scp`):

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\package-site.ps1
   scp -r dist\site\* USER@SERVER-IP:/var/www/hardhatmediaplayer/
   ```

The firewall must allow ports 80 and 443 (`sudo ufw allow 80,443/tcp` if you use ufw).

### c. Point the domain at your server

At the company where you bought the domain, open the DNS settings for `hardhatmediaplayer.com`. If the domain
and the hosting are at the same company, this is often already done; check with the commands below first.
Otherwise **delete** any default `@` and `www` records (parking page, URL forwarding) and add:

| Type  | Name / Host | Value |
| ----- | ----------- | ----- |
| A     | `@`   | your server's IPv4 address |
| AAAA  | `@`   | your server's IPv6 address (only if it has one) |
| CNAME | `www` | `hardhatmediaplayer.com` |

If your host gave you **nameservers** (like `ns1.yourhost.com`) instead of an IP, put those under
**Nameservers** at the registrar instead, and the host manages the records.

Check (changes can take up to 24 hours to spread):

```powershell
Resolve-DnsName hardhatmediaplayer.com -Type A
Resolve-DnsName www.hardhatmediaplayer.com
```

When the address is your server's, open <https://hardhatmediaplayer.com> and check the padlock.

### Changing the site later

1. Edit the files in `site/` and commit (the repository stays the source of the site).
2. Run `scripts\package-site.ps1` again.
3. Upload as in b1 (extract the new zip over the old files) or b2 (the same `scp` command).

To add the real screenshot, save it as `site/screenshot.png` and replace the placeholder `<div>` in
`index.html` as its comment describes.

Downloads of the app itself stay on **GitHub Releases** (step 7); the site only links to them, so the ~260 MB
files never use your server's traffic.

## 9. Day to day

- **Your own changes:** commit as usual, then `git push`.
- **Contributions:** people fork, change, and open a **pull request**. The build check runs. You review
  and press **Merge**.
- **Bugs and ideas** arrive as **Issues**. Label them (`bug`, `enhancement`, `good first issue`) so newcomers
  know where to start.
