import 'package:flutter/material.dart';
import 'package:share_plus/share_plus.dart';

import 'client_generator.dart';
import 'knowledge.dart';
import 'main.dart';
import 'user_data.dart';
import 'widgets.dart';

Future<void> _shareCode(BuildContext context, String code, String fileName) async {
  try {
    await SharePlus.instance.share(ShareParams(text: code, subject: fileName));
  } catch (_) {
    if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Nothing on this phone can take the share')));
  }
}

/// Generated code in a box that scrolls both ways, selectable.
class _CodeBox extends StatelessWidget {
  final String code;
  const _CodeBox(this.code);

  @override
  Widget build(BuildContext context) => Container(
        width: double.infinity,
        padding: const EdgeInsets.all(10),
        decoration: BoxDecoration(border: Border.all(color: Theme.of(context).colorScheme.outlineVariant), borderRadius: BorderRadius.circular(4)),
        child: SingleChildScrollView(scrollDirection: Axis.horizontal, child: SelectableText(code, style: mono.copyWith(fontSize: 12, height: 1.45))),
      );
}

/// A page of generated C#: classes for one response, or the client for a whole collection.
class CodePage extends StatelessWidget {
  final String title;
  final String fileName;
  final String code;
  final String? note;
  const CodePage({super.key, required this.title, required this.fileName, required this.code, this.note});

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          toolbarHeight: 48,
          title: Text(title),
          actions: [
            IconButton(tooltip: 'Copy the code', icon: const Icon(Icons.copy), onPressed: () => copyText(context, code, fileName)),
            IconButton(tooltip: 'Share the code', icon: const Icon(Icons.share_outlined), onPressed: () => _shareCode(context, code, fileName)),
          ],
        ),
        body: ListView(padding: EdgeInsets.fromLTRB(16, 4, 16, 24 + MediaQuery.viewPaddingOf(context).bottom), children: [
          if (note != null) Padding(padding: const EdgeInsets.only(bottom: 4), child: Text(note!, style: TextStyle(fontSize: 12.5, height: 1.4, color: Theme.of(context).colorScheme.onSurfaceVariant))),
          FieldLabel(fileName.toUpperCase()),
          _CodeBox(code),
        ]),
      );
}

/// "C# client": tick which of the requests that worked become methods; the class below follows the ticks.
class ClientPage extends StatefulWidget {
  final ApiView view;

  /// The API's request history and variables; read from the app's state when not given (a test gives its own).
  final List<TestHistoryEntry>? history;
  final Map<String, String>? variables;
  const ClientPage({super.key, required this.view, this.history, this.variables});
  @override
  State<ClientPage> createState() => _ClientPageState();
}

class _ClientPageState extends State<ClientPage> {
  late final List<TestHistoryEntry> _history = widget.history ?? state.historyOf(widget.view);
  late final List<TestHistoryEntry> _worked = usableRequests(_history);
  // to start with: the newest test of each distinct request
  late final Set<TestHistoryEntry> _picked = {...newestOfEachRequest(_history)};

  String get _fileName => '${clientClassName(widget.view.name)}.cs';

  String? get _code => _picked.isEmpty
      ? null
      : generateClient(widget.view.name, _worked.where(_picked.contains), demoKey: widget.view.demoKey, variables: widget.variables ?? state.variablesOf(widget.view));

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final muted = TextStyle(fontSize: 12.5, height: 1.4, color: scheme.onSurfaceVariant);
    final code = _code;
    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 48,
        title: const Text('C# client'),
        actions: [
          IconButton(tooltip: 'Copy the code', icon: const Icon(Icons.copy), onPressed: code == null ? null : () => copyText(context, code, _fileName)),
          IconButton(tooltip: 'Share the code', icon: const Icon(Icons.share_outlined), onPressed: code == null ? null : () => _shareCode(context, code, _fileName)),
        ],
      ),
      body: ListView(padding: EdgeInsets.fromLTRB(16, 4, 16, 24 + MediaQuery.viewPaddingOf(context).bottom), children: [
        Text(
          'Each ticked request becomes one method of a small HttpClient class for ${widget.view.name}. Two tests of the same endpoint make one method - the newer one wins. '
          'Your key is never written into the code: {key} becomes a constructor parameter. Test more endpoints in Try it to have more to pick from.',
          style: muted,
        ),
        const SizedBox(height: 6),
        for (final h in _worked)
          CheckboxListTile(
            dense: true,
            contentPadding: EdgeInsets.zero,
            controlAffinity: ListTileControlAffinity.leading,
            value: _picked.contains(h),
            onChanged: (on) => setState(() => on == true ? _picked.add(h) : _picked.remove(h)),
            title: Text('${h.method}  ${h.url}', maxLines: 2, overflow: TextOverflow.ellipsis, style: mono.copyWith(fontSize: 12)),
            subtitle: Text('${h.when}  ·  ${h.firstLine}', maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 11.5)),
          ),
        Row(children: [
          TextButton(onPressed: () => setState(() => _picked.addAll(_worked)), child: const Text('Tick all')),
          TextButton(onPressed: () => setState(_picked.clear), child: const Text('None')),
        ]),
        FieldLabel(_fileName.toUpperCase()),
        if (code == null) Text('Tick at least one request.', style: muted) else _CodeBox(code),
      ]),
    );
  }
}
