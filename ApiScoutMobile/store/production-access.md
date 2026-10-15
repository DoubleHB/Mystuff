# Applying for production access - draft answers

Google asks these questions (wording varies a little) when you press "Apply for production access" after the
closed test. Everything below is true as of 15 October 2026; change anything that does not match what you did.
Google reads these for sincerity, not polish - short, honest answers are fine.

## About the closed test

**How easy or difficult was it to recruit testers?**
Fairly easy once I used a testing service. I do not have a user base yet, so I paid a company that provides
Google Play testers; they opted 12 testers in and kept them opted in for the full 14 days.

**How did you recruit your testers?**
Through a paid Play testing service. The testers installed the app from the closed testing link and kept it
installed for the test period.

**How did you engage with your testers and collect feedback?**
The testers could reach me by the email address on the listing and through the service's feedback channel. I
also watched the Play Console for crashes and ANRs, and checked that installs, the first scan, the daily
notification and updates between versions (1.5.1 to 1.5.5) worked on their devices.

**Summarise the feedback you received.**
The testers reported no crashes or problems. I did not receive feature requests from them. Most of the
improvements during the test came from my own walkthrough of every screen on several Android versions and text
sizes, which the test period gave me time to do.

**What did you change as a result of the test?**
- Fixed a long-standing bug where the app's bundled typefaces never loaded on Android, so every screen was drawn
  in the system font (1.5.3).
- Large system text sizes: list rows no longer touch their dividers and tabs scroll instead of being cut off.
- Clearer messages when a search finds nothing, and correct "API key" wording for APIs with a demo key.
- A fresh install now scans by itself instead of opening on an empty page (1.5.4).
- A "Works right now" filter, remembered filters, duplicate entries merged into one row, sort by health, and
  copying a request as cURL, JavaScript or Python (1.5.4 and 1.5.5).

## About the app

**Who is the intended audience?**
Developers, students and hobbyists who want to find a free public API for a project and try it from their
phone before writing code: what the API needs, whether there is a demo key, and a working example request.

**Describe your app's core value.**
It scans five public API directories, merges them into one catalogue of about 3,600 APIs with 42 categories,
shows whether each one needs a key (with the provider's own published demo key where there is one), and lets
you send a test request from the phone. Favourites, tags, notes and collections move to and from the desktop
version. No account, no adverts, no analytics; the only network traffic is to the directories and to the APIs
you choose to test.

**Is your app ready for production? Why?**
Yes. Five versions went through closed testing without a crash report. The app has automated tests for its
scanner, categoriser, key knowledge and export format, and a live test against the real directories. The
listing, privacy policy, content rating and data safety form are complete.

## After you submit

Google usually answers within about 7 days. If they ask for more detail, they want specifics: numbers of
testers, dates, concrete changes. The list above has them.
