// Moving data between the desktop app and the phone: the Import / Export menu items and their dialogs.
import 'dart:convert';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'main.dart';
import 'user_data.dart';

/// A spinner the Back button cannot dismiss, closed by its own route - never by popping whatever is on top at the time.
class WorkingDialog {
  BuildContext? _dialog;
  bool _closed = false;

  void close() {
    _closed = true;
    final c = _dialog;
    if (c != null && c.mounted) Navigator.of(c).pop();
  }
}

WorkingDialog showWorking(BuildContext context) {
  final w = WorkingDialog();
  showDialog(
    context: context,
    barrierDismissible: false,
    builder: (c) {
      w._dialog = c;
      // the work finished before the dialog was even built: leave again at once
      if (w._closed) WidgetsBinding.instance.addPostFrameCallback((_) => c.mounted ? Navigator.of(c).pop() : null);
      return const PopScope(canPop: false, child: Center(child: CircularProgressIndicator()));
    },
  );
  return w;
}

/// A line at the bottom of the screen, a little longer than a copy's tick.
void say(BuildContext context, String text) {
  if (!context.mounted) return;
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(text), duration: const Duration(seconds: 5)));
}

/// Asks for a passphrase. Pops null for Cancel, '' for "without the keys".
Future<String?> _askPassphrase(BuildContext context, {required String title, required String explanation, required String skipLabel, bool confirm = false}) {
  final first = TextEditingController(), second = TextEditingController();
  String? problem;
  return showDialog<String>(
    context: context,
    builder: (context) => StatefulBuilder(
      builder: (context, setState) => AlertDialog(
        title: Text(title),
        content: SingleChildScrollView(
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(explanation),
            const SizedBox(height: 14),
            TextField(controller: first, obscureText: true, autocorrect: false, enableSuggestions: false, decoration: const InputDecoration(labelText: 'Passphrase')),
            if (confirm) ...[
              const SizedBox(height: 10),
              TextField(controller: second, obscureText: true, autocorrect: false, enableSuggestions: false, decoration: const InputDecoration(labelText: 'The same again')),
            ],
            if (problem != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text(problem!, style: TextStyle(color: Theme.of(context).colorScheme.error))),
          ]),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancel')),
          TextButton(onPressed: () => Navigator.of(context).pop(''), child: Text(skipLabel)),
          FilledButton(
              onPressed: () {
                if (first.text.isEmpty) {
                  setState(() => problem = 'Type the passphrase, or choose "$skipLabel".');
                } else if (confirm && first.text.length < 8) {
                  setState(() => problem = 'Use at least 8 characters: the passphrase is all that protects the keys in the file.');
                } else if (confirm && first.text != second.text) {
                  setState(() => problem = 'The two do not match.');
                } else {
                  Navigator.of(context).pop(first.text);
                }
              },
              child: const Text('OK')),
        ],
      ),
    ),
  );
}

/// Menu → Import: a file made by the desktop app's "Export my data" (or by this app).
Future<void> importFromFile(BuildContext context) async {
  try {
    final picked = await FilePicker.pickFile(dialogTitle: 'Pick the API Free export file', type: FileType.any);
    if (picked == null || !context.mounted) return;
    final size = await picked.length() ?? 0;
    if (!context.mounted) return;
    if (size > 20 * 1024 * 1024) return say(context, 'That file is far too big to be an API Free export.');
    final json = utf8.decode(await picked.readAsBytes(), allowMalformed: true);
    final file = BackupFile.read(json); // throws when it is some other file
    String? passphrase;
    if (file.hasSecrets) {
      if (!context.mounted) return;
      passphrase = await _askPassphrase(context,
          title: 'This file holds ${file.secretCount} encrypted key(s) / test request(s)',
          explanation: 'The phone takes the keys. Type the passphrase that was chosen when the file was exported and they go into this phone\'s secure storage. Unlocking takes a few seconds.',
          skipLabel: 'Skip the keys');
      if (passphrase == null) return;
    }
    if (!context.mounted) return;
    final working = showWorking(context);
    ImportSummary summary;
    try {
      summary = await state.importBackup(json, passphrase);
    } finally {
      working.close();
    }
    if (!context.mounted) return;
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Imported'),
        content: Text('$summary\n\nNothing that was already on this phone was changed or removed.'),
        actions: [FilledButton(onPressed: () => Navigator.of(context).pop(), child: const Text('OK'))],
      ),
    );
  } on BackupFormatException catch (ex) {
    if (context.mounted) say(context, ex.message);
  } on WrongPassphraseException catch (ex) {
    if (context.mounted) say(context, '$ex Nothing was imported.');
  } catch (ex) {
    if (context.mounted) say(context, 'Could not import: $ex');
  }
}

/// Menu → Export: the same file format, so the desktop app's Import reads it.
Future<void> exportToFile(BuildContext context) async {
  if (state.userItemCount == 0 && state.keyed.isEmpty) return say(context, 'Nothing to export yet: no favourites, tags, notes, collections, variables or keys.');
  try {
    String? passphrase = '';
    if (state.secretItemCount > 0) {
      passphrase = await _askPassphrase(context,
          title: 'Include your saved keys and request variables?',
          explanation: 'This phone holds ${state.keyed.length} key(s) and variables for ${state.user.variables.length} API(s). With a passphrase they go into the file encrypted (AES-256); the desktop app asks for the same passphrase when it imports. Without one they stay out of the file.',
          skipLabel: 'Without the keys',
          confirm: true);
      if (passphrase == null) return;
    }
    if (!context.mounted) return;
    final working = showWorking(context);
    String json;
    try {
      json = await state.exportBackup(passphrase);
    } finally {
      working.close();
    }
    final now = DateTime.now();
    final saved = await FilePicker.saveFile(
      fileName: 'apifree-phone-${now.year}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}.json',
      bytes: Uint8List.fromList(utf8.encode(json)),
      mimeType: 'application/json',
    );
    if (saved != null && context.mounted) say(context, '✓ Exported. On the PC: API Free → About → Import…, and pick this file.');
  } catch (ex) {
    if (context.mounted) say(context, 'Could not export: $ex');
  }
}
