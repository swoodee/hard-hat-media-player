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

4. Check that no commit still carries your personal email (only matters before the first push; this was
   already done for the history up to October 2026):

   ```powershell
   git log --format="%h %an <%ae> / %cn <%ce>  %s"
   ```

   If some lines show a personal address, rewrite them to the private one. Put your own addresses in
   the first lines; the rest stays as it is:

   ```powershell
   $env:FILTER_BRANCH_SQUELCH_WARNING = 1
   $old = "personal.address@example.com"
   $new = "12345678+YOUR-USERNAME@users.noreply.github.com"
   git filter-branch --env-filter "if [ `"`$GIT_AUTHOR_EMAIL`" = '$old' ]; then export GIT_AUTHOR_NAME='YOUR-USERNAME' GIT_AUTHOR_EMAIL='$new'; fi; if [ `"`$GIT_COMMITTER_EMAIL`" = '$old' ]; then export GIT_COMMITTER_NAME='YOUR-USERNAME' GIT_COMMITTER_EMAIL='$new'; fi" -- master
   ```

   Use this, not `git rebase --root`: the history contains a merge, and a rebase replays it and stops on
   a conflict. `filter-branch` only changes the names and emails; the files stay exactly the same.
   It keeps a backup of the old history; once the log looks right, remove it:

   ```powershell
   git update-ref -d refs/original/refs/heads/master
   ```

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

## 7. Publish a release (installer + portable zip)

Releases are built by GitHub: pushing a version tag runs `.github/workflows/release.yml`, which builds the
installer and the portable zip with `scripts/build-release.ps1` and publishes a release with both attached.
The notes come from `.github/release-notes.md`.

1. Set `<Version>` in `src/HardHatMediaPlayer/HardHatMediaPlayer.csproj` (for example `0.2.0`), commit and push.
2. Tag that commit and push the tag:

   ```powershell
   git tag v0.2.0
   git push origin v0.2.0
   ```

3. Watch **Actions → Release**; after a few minutes the release appears under **Releases**.

The "Source code" archives GitHub adds to every release leave out `site/`, `docs/` and the website script
(see `.gitattributes`), so they contain only the app.

To build the same files on your PC first (needs [Inno Setup](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File scriptsuild-release.ps1
```

They land in `artifacts\`. The installer isn't code-signed, so Windows SmartScreen may warn people on first run
("More info → Run anyway"). Code signing can come later.

## 8. Put the website on hardhatmediaplayer.com (home server)

The website is the `site/` folder: plain HTML, CSS and an SVG icon, with no build step, no database and no PHP.
It runs on the home Proxmox server in its own small container with nginx, and reaches the internet through
a **Cloudflare Tunnel**: no ports are opened on the router, and Cloudflare provides HTTPS. You publish it by
hand: change the files, package them, upload them.

```
visitor ──HTTPS──> Cloudflare ──tunnel──> cloudflared (already running) ──HTTP──> "web" container: nginx :80
```

**First**, replace every `YOUR-USERNAME` in `site/index.html` with your GitHub username. To preview the site,
open `site/index.html` in a browser.

The placeholders below: `WEB-IP` is the address you give the new container, `ROUTER-IP` your router (the
gateway the other containers use).

### a. Move the domain's DNS to Cloudflare (once)

A tunnel can only publish a domain whose DNS Cloudflare manages.

1. In the Cloudflare dashboard: **Add a domain**, enter `hardhatmediaplayer.com`, choose the **Free** plan.
2. Cloudflare shows two nameservers (like `xxx.ns.cloudflare.com`). At the company where you bought the domain,
   replace its nameservers with those two.
3. Wait until Cloudflare shows the domain as **Active** (minutes to a few hours; it emails you).
4. **SSL/TLS → Edge Certificates:** turn on **Always Use HTTPS**.

### b. Create the "web" container in Proxmox

Use a separate container, not the one with the AI services, so the public website can't reach them.

1. On your PC, make an SSH key if you don't have one (press Enter at the questions):

   ```powershell
   ssh-keygen -t ed25519
   Get-Content $HOME\.ssh\id_ed25519.pub
   ```

2. In the Proxmox web interface: **local → CT Templates → Templates**, download **debian-12-standard**.
3. **Create CT**:
   - **General:** hostname `web`, **Unprivileged container** ticked, a root password, and paste the `.pub` line
     from step 1 under **SSH public key**.
   - **Template:** the Debian 12 template. **Disks:** 4 GB. **CPU:** 1 core. **Memory:** 512 MB, swap 512 MB.
   - **Network:** IPv4 **Static**, `WEB-IP/24` (a free address on your network), gateway `ROUTER-IP`.
     A fixed address matters: the tunnel points to it.
   - **Confirm:** tick **Start after created**.
4. In the container's **Options**, set **Start at boot** to Yes.

### c. Install nginx in the container

In the container's **Console** in Proxmox (or `ssh root@WEB-IP`):

```bash
apt update && apt full-upgrade -y
apt install -y nginx unattended-upgrades
mkdir -p /var/www/hardhatmediaplayer
rm /etc/nginx/sites-enabled/default
nano /etc/nginx/sites-available/hardhatmediaplayer
```

Paste this, save with Ctrl+O, Enter, Ctrl+X:

```nginx
server {
    listen 80 default_server;
    server_name hardhatmediaplayer.com;
    root /var/www/hardhatmediaplayer;
    index index.html;
    server_tokens off;
    location / { try_files $uri $uri/ =404; }
}

server {
    listen 80;
    server_name www.hardhatmediaplayer.com;
    return 301 https://hardhatmediaplayer.com$request_uri;
}
```

Turn it on:

```bash
ln -s /etc/nginx/sites-available/hardhatmediaplayer /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx
```

`unattended-upgrades` installs Debian's security updates automatically.

### d. Upload the site

From the repository folder on your PC (Windows 10/11 include `scp`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-site.ps1
scp -r dist\site\* root@WEB-IP:/var/www/hardhatmediaplayer/
```

Check it on your home network: open `http://WEB-IP` in a browser.

### e. Publish it through the tunnel

Add two public hostnames to the tunnel that already runs on the server.

**If the tunnel is managed in the dashboard** (it was installed with `cloudflared service install <token>`):
Cloudflare **Zero Trust → Networks → Tunnels →** your tunnel **→ Edit → Public Hostname → Add a public hostname**:

| Subdomain | Domain | Service type | URL |
| --------- | ------ | ------------ | --- |
| *(empty)* | `hardhatmediaplayer.com` | HTTP | `WEB-IP:80` |
| `www`     | `hardhatmediaplayer.com` | HTTP | `WEB-IP:80` |

Cloudflare creates the DNS records itself.

**If the tunnel uses a config file** (`/etc/cloudflared/config.yml` with `ingress:` rules), add these two rules
above the final `- service: http_status:404` line:

```yaml
  - hostname: hardhatmediaplayer.com
    service: http://WEB-IP:80
  - hostname: www.hardhatmediaplayer.com
    service: http://WEB-IP:80
```

then run, in the container with the tunnel:

```bash
cloudflared tunnel route dns <TUNNEL-NAME> hardhatmediaplayer.com
cloudflared tunnel route dns <TUNNEL-NAME> www.hardhatmediaplayer.com
systemctl restart cloudflared
```

Open <https://hardhatmediaplayer.com> and check the padlock; `www.` should redirect to it.

### Changing the site later

1. Edit the files in `site/` and commit (the repository stays the source of the site).
2. Run `scripts\package-site.ps1` and the same `scp` command as in d.
3. Cloudflare caches the CSS and images for a while. When you change `style.css`, raise the number in
   `style.css?v=2` in `index.html`; for a replaced image, give the file a new name. Otherwise use
   **Caching → Configuration → Purge Everything**.

The screenshot (`site/screenshot.webp`, plus `screenshot-1000.webp` for phones) shows Big Buck Bunny, which is
CC BY 3.0: keep the credit under it.

Downloads of the app itself stay on **GitHub Releases** (step 7); the site only links to them, so the ~260 MB
files never go through your home connection. If the server is off, the site is down too: Cloudflare shows an
error page until it is back.

## 9. Day to day

- **Your own changes:** commit as usual, then `git push`.
- **Contributions:** people fork, change, and open a **pull request**. The build check runs. You review
  and press **Merge**.
- **Bugs and ideas** arrive as **Issues**. Label them (`bug`, `enhancement`, `good first issue`) so newcomers
  know where to start.
