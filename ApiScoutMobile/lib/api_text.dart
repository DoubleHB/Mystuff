// An API as plain text and as Markdown: the "Copy this API as" buttons and the share sheet. Next to compareMarkdown
// and insightMarkdown, so every piece of text the app hands out is written in one kind of place.
import 'knowledge.dart';

/// Plain lines: name, description, category, docs, auth, free access, the provider's demo key, example, sign-up.
/// The user's own key is never part of it.
String apiText(ApiView v) => [
      v.name,
      v.entry.description,
      'Category: ${v.entry.category}',
      'Docs: ${v.url}',
      'Auth: ${v.authLabel}  ·  ${v.keyHeadline}',
      'Free access: ${v.accessLabel}',
      if (v.hasDemoKey) 'Demo key: ${v.demoKey}',
      if (v.hint?.keyUsage != null) 'How the key is sent: ${v.hint!.keyUsage}',
      if (v.example != null) 'Example: ${v.example}',
      if (v.hint?.signupUrl != null) 'Get a key: ${v.hint!.signupUrl}',
    ].join('\n');

/// The Markdown copy, handed to Android's share sheet (a chat, a note, an email). It carries the provider's
/// published demo key where there is one and never the user's own key.
String apiMarkdown(ApiView v) => [
      '### [${v.name}](${v.url})',
      '',
      v.entry.description,
      '',
      '- **Category:** ${v.entry.category}',
      '- **Auth:** ${v.authLabel} - ${v.keyHeadline}',
      '- **Free access:** ${v.accessLabel}',
      if (v.hasDemoKey) '- **Demo key:** `${v.demoKey}`',
      if (v.example != null) '- **Example:** `${v.example}`',
      if (v.hint?.signupUrl != null) '- **Get a key:** ${v.hint!.signupUrl}',
    ].join('\n');
