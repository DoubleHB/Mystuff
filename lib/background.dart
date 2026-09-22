// The weekly rescan while the app is closed: Android WorkManager runs [backgroundDispatcher] in its own engine,
// which reads the same sources with the same rules and leaves catalogue.json + changes.json for the next start.
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart' show rootBundle;
import 'package:path_provider/path_provider.dart';
import 'package:workmanager/workmanager.dart';

import 'changes.dart';
import 'knowledge.dart';
import 'models.dart';
import 'sources.dart';

const rescanTask = 'apiscout-weekly-rescan';

@pragma('vm:entry-point')
void backgroundDispatcher() {
  Workmanager().executeTask((task, input) async {
    try {
      await rescanInBackground();
      return true;
    } catch (_) {
      return false;
    }
  });
}

Future<void> enableWeeklyRescan() => Workmanager().registerPeriodicTask(
      rescanTask,
      rescanTask,
      frequency: const Duration(days: 7),
      initialDelay: const Duration(days: 7),
      constraints: Constraints(networkType: NetworkType.unmetered, requiresBatteryNotLow: true),
      existingWorkPolicy: ExistingPeriodicWorkPolicy.keep,
    );

Future<void> disableWeeklyRescan() => Workmanager().cancelByUniqueName(rescanTask);

/// A save that is cut short leaves the old file whole.
Future<void> writeFileAtomically(File file, String text) async {
  final temp = File('${file.path}.tmp');
  await temp.writeAsString(text, flush: true);
  await temp.rename(file.path);
}

/// The scan the app makes, without the app. A thin result (most sources down) is thrown away rather than
/// replacing a good catalogue; the diff against the old catalogue waits in changes.json.
Future<void> rescanInBackground() async {
  final knowledgeJson = await rootBundle.loadString('assets/knowledge.json');
  final dir = (await getApplicationSupportDirectory()).path;
  final texts = <String, String>{};
  final notes = <String>[];
  await Future.wait(sources.map((s) async {
    try {
      texts[s.id] = await fetchText(s.url);
    } catch (ex) {
      notes.add('${s.name}: failed - $ex');
    }
  }));
  if (texts.length < 3) return;
  final result = buildCatalogue({'knowledge': knowledgeJson, 'texts': texts, 'notes': notes});
  final entries = [for (final e in (result['entries'] as List)) ApiEntry.fromJson((e as Map).cast<String, dynamic>())];
  if (entries.length < 500) return;
  final file = File('$dir/catalogue.json');
  var before = <ApiEntry>[];
  if (await file.exists()) {
    try {
      before = Catalogue.fromJson(jsonDecode(await file.readAsString()) as Map<String, dynamic>).entries;
    } catch (_) {}
  }
  await writeFileAtomically(file, jsonEncode(Catalogue(DateTime.now(), entries, rules: rulesHash(knowledgeJson)).toJson()));
  if (before.isNotEmpty) await writeFileAtomically(File('$dir/changes.json'), jsonEncode(diffCatalogues(before, entries).toJson()));
}
