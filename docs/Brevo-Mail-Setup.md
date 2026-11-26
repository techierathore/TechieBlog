# Sending the blog's mail through Brevo

The blog only sends mail (confirmation links, password resets, the newsletter). It never receives any. So it needs a sending service, not a mailbox and not a mail server on the virtual machine. Brevo's free plan sends up to 300 mails a day, by Brevo's own page, with no card.

With Brevo the reader sees `verification@techierathore.com` as the sender, not a Gmail address.

Time: about 20 minutes of clicks, then a wait for the domain records to be seen. Brevo's guide says that wait can be up to 24 to 48 hours; it is often much shorter. The blog keeps working on Gmail until you switch.

## What you need before you start

- The login of the place where the DNS records of `techierathore.com` are managed (your domain registrar or DNS provider).
- The blog deployed with the "Set up mail" screen (Settings › Email, with the "Send test mail" button).

## Step 1. Make the Brevo account

1. Open <https://www.brevo.com/free-smtp-server/> and sign up with your own mail address.
2. Confirm the address from the mail Brevo sends you and finish the short sign-up form.

## Step 2. Tell Brevo the domain is yours

1. In Brevo click your name (top right) › **Settings** › **Senders, Domains, IPs** › **Domains**.
2. Click **Add a domain**, type `techierathore.com`.
3. Brevo offers two ways:
   - **Automatic:** Brevo recognises your DNS provider, you sign in to it there, and Brevo adds the records itself. Take this if it is offered.
   - **Manual:** Brevo shows three records. Leave this page open and do step 3.

## Step 3. Add the records (manual way only)

Open your DNS provider in a second tab, go to the DNS records of `techierathore.com`, and add what Brevo shows. Copy every name and value from Brevo's page exactly; the values below only show the shape.

| Record | Type | Name (host) | Value |
|---|---|---|---|
| Brevo code | TXT | `@` (the domain itself) | `brevo-code:…` as shown by Brevo |
| DKIM | TXT, or two CNAME records | as shown by Brevo (for example `brevo1._domainkey`) | as shown by Brevo |
| DMARC | TXT | `_dmarc` | `v=DMARC1; p=none; rua=mailto:rua@dmarc.brevo.com` |

Two things to watch:

- If `techierathore.com` already has a `_dmarc` record, do not add a second one. Add only `rua=mailto:rua@dmarc.brevo.com` to the end of the one that exists.
- These records do not touch the website. The A record that points the domain at your virtual machine stays as it is.

Back in Brevo click **Authenticate this email domain** (or **Verify**). When all three rows are green, the domain is done. If one row stays red after a few hours, Brevo's troubleshooting page (linked at the end) lists the usual causes; the common one is the DNS provider adding the domain name a second time to the host name.

## Step 4. Add the sender

1. Still under **Senders, Domains, IPs**, open **Senders** › **Add a sender**.
2. From name: `TechieRathore Blog`. From email: `verification@techierathore.com`.

This address does not have to exist as a mailbox. The confirmation mail already tells the reader not to reply.

## Step 5. Get the sending login

1. Click your name › **Settings** › **SMTP & API** › the **SMTP** tab.
2. Note the three values shown there:
   - **SMTP server:** `smtp-relay.brevo.com`
   - **Login:** either your Brevo login address or an address like `1234567@smtp-brevo.com`. Use the one the page shows.
   - **SMTP key:** click **Generate a new SMTP key**, name it `techieblog`, and copy the key at once. Brevo shows it only one time. This is not the API key and not your Brevo password.

## Step 6. Enter it in the blog

1. Open BlogApp, connected to the live blog, and open **Settings** › **Email**. (The website's own admin has the same screen.)
2. Fill in:

| Box | Value |
|---|---|
| Host | `smtp-relay.brevo.com` |
| Port | `587` |
| Use SSL/TLS | ticked |
| Username | the Login from step 5 |
| Password | the SMTP key from step 5 |
| From address | `verification@techierathore.com` |
| From name | `TechieRathore Blog` |

3. Under **Send test mail** type your own address and click **Send test mail**.
   - Green answer: check your mailbox, and the spam folder the first time.
   - "No answer from … within 30 seconds": the server's firewall or its hosting company blocks outgoing port 587. Change Port to `2525`, which Brevo also accepts, and test again.
   - An answer about encryption: untick Use SSL/TLS, test again, and tell the agent in the next run; Brevo's page says port 587 needs no encryption setting.
4. Click **Save Settings**. BlogApp writes the values into the blog's database; no code change and no deploy is needed. The live site uses them within ten minutes.

The test mail from BlogApp goes out from your own computer. It proves the Brevo login; step 7 proves the server.

Port 465 does not work with this blog; use 587 or 2525.

## Step 7. Prove it as a reader

1. Ten minutes after saving, open any post in a private window and rate it with an address you can read.
2. The form says "We sent a link to …". Open the mail: the sender is `verification@techierathore.com`.
3. Click the link, reload the post: the rating count is one higher.

## Going back to Gmail

Enter the Gmail values from "Set up mail" in the usage guide on the same screen in BlogApp and save. Nothing else changes.

## Brevo's own pages

- Free plan and limits: <https://www.brevo.com/free-smtp-server/>
- Sending through SMTP: <https://help.brevo.com/hc/en-us/articles/7924908994450-Send-transactional-emails-using-Brevo-SMTP>
- SMTP settings and ports: <https://developers.brevo.com/docs/smtp-integration>
- Authenticating a domain: <https://help.brevo.com/hc/en-us/articles/12163873383186-Authenticate-your-domain-with-Brevo-Brevo-code-DKIM-DMARC>
- When a record stays red: <https://help.brevo.com/hc/en-us/articles/16045394674066-Troubleshooting-issues-with-domain-authentication-Brevo-code-DKIM-DMARC>
