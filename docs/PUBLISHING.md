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

## 8. Point hardhatmediaplayer.com at the project (optional)

The simplest option is to make the domain redirect to the GitHub repository in your domain registrar's
settings (**URL forwarding** to `https://github.com/YOUR-USERNAME/hard-hat-media-player`).

For a real website later, GitHub Pages can host it for free with your domain. Its documentation, *Managing a
custom domain for your GitHub Pages site*, lists the DNS records to add at your registrar.

## 9. Day to day

- **Your own changes:** commit as usual, then `git push`.
- **Contributions:** people fork, change, and open a **pull request**. The build check runs. You review
  and press **Merge**.
- **Bugs and ideas** arrive as **Issues**. Label them (`bug`, `enhancement`, `good first issue`) so newcomers
  know where to start.
