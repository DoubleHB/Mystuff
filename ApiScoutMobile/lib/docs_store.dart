// Results of "Scan docs for key info": public information, kept as plain JSON, the latest few hundred.
import 'dart:convert';
import 'dart:io';

import 'package:path_provider/path_provider.dart';

import 'docs_scanner.dart';
import 'models.dart';

class DocsStore {
  static const keep = 300;

  Map<String, DocsScanResult> _scans = {};
  final scanning = <String>{};
  Future<void> _pending = Future.value();

  Future<File> _file() async => File('${(await getApplicationSupportDirectory()).path}/docscans.json');

  Future<void> load() async {
    try {
      final file = await _file();
      if (!await file.exists()) return;
      final j = jsonDecode(await file.readAsString());
      if (j is Map) _scans = {for (final e in j.entries) if (e.value is Map) e.key as String: DocsScanResult.fromJson((e.value as Map).cast<String, dynamic>())};
    } catch (_) {
      _scans = {};
    }
  }

  DocsScanResult? scanOf(String apiKey) => _scans[apiKey];
  bool isScanning(String apiKey) => scanning.contains(apiKey);

  /// Scans the docs and keeps the result; null when a scan of this API is already running. The key is marked as
  /// scanning before the first await, so a caller can show the spinner right after calling.
  Future<DocsScanResult?> scan(ApiEntry entry, {String? exampleUrl}) async {
    if (!scanning.add(entry.key)) return null;
    try {
      final result = await scanDocs(entry, exampleUrl: exampleUrl);
      _scans[entry.key] = result;
      // only the latest few hundred are worth keeping on a phone
      if (_scans.length > keep) {
        final oldest = _scans.entries.toList()..sort((a, b) => a.value.scannedAt.compareTo(b.value.scannedAt));
        for (final e in oldest.take(_scans.length - keep)) {
          _scans.remove(e.key);
        }
      }
      _save();
      return result;
    } finally {
      scanning.remove(entry.key);
    }
  }

  void _save() => _pending = _pending.then((_) async {
        try {
          final file = await _file();
          final temp = File('${file.path}.$pid.tmp');
          await temp.writeAsString(jsonEncode({for (final e in _scans.entries) e.key: e.value.toJson()}), flush: true);
          await temp.rename(file.path);
        } catch (_) {}
      });
}
