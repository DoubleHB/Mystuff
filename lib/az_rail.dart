import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// The rail's bucket for a name: its first letter in capitals, or '#' for anything that is not A-Z.
String initialOf(String name) {
  if (name.isEmpty) return '#';
  final c = name.codeUnitAt(0);
  if (c >= 0x61 && c <= 0x7A) return String.fromCharCode(c - 32);
  if (c >= 0x41 && c <= 0x5A) return name[0];
  return '#';
}

/// Where each initial first appears, in the order the initials turn up. Meaningful only for a list sorted by name.
List<(String, int)> firstIndexByInitial(Iterable<String> names) {
  final first = <String, int>{};
  var i = 0;
  for (final n in names) {
    first.putIfAbsent(initialOf(n), () => i);
    i++;
  }
  return [for (final e in first.entries) (e.key, e.value)];
}

/// Width of the column the rail lives in; rows keep this much clear on the right.
const railWidth = 22.0;

/// A column of initials at the edge of a long list: tap or drag to jump, a bubble shows the letter under the finger.
/// [current] is the initial of the row at the top of the list, shown in the accent colour while nothing is held.
class AlphabetRail extends StatefulWidget {
  final List<String> letters;
  final ValueListenable<String?> current;
  final void Function(String letter) onLetter;
  const AlphabetRail({super.key, required this.letters, required this.current, required this.onLetter});

  @override
  State<AlphabetRail> createState() => _AlphabetRailState();
}

class _AlphabetRailState extends State<AlphabetRail> {
  String? _held;
  double _heldY = 0;

  void _at(Offset local, double step) {
    final i = (local.dy / step).floor().clamp(0, widget.letters.length - 1);
    final l = widget.letters[i];
    if (l != _held) {
      HapticFeedback.selectionClick();
      widget.onLetter(l);
    }
    setState(() {
      _held = l;
      _heldY = i * step + step / 2;
    });
  }

  void _release() {
    if (_held != null) setState(() => _held = null);
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final n = widget.letters.length;
    if (n == 0) return const SizedBox.shrink();
    return LayoutBuilder(builder: (context, box) {
      final step = (box.maxHeight / n).clamp(9.0, 14.0);
      return Center(
        child: SizedBox(
          width: railWidth,
          height: step * n,
          child: Stack(clipBehavior: Clip.none, children: [
            // raw pointer events: a tap jumps at once, and no scroll gesture competes for the drag
            Listener(
              behavior: HitTestBehavior.opaque,
              onPointerDown: (e) => _at(e.localPosition, step),
              onPointerMove: (e) => _at(e.localPosition, step),
              onPointerUp: (_) => _release(),
              onPointerCancel: (_) => _release(),
              child: ValueListenableBuilder<String?>(
                valueListenable: widget.current,
                builder: (_, current, _) {
                  final lit = _held ?? current;
                  return Column(children: [
                    for (final l in widget.letters)
                      SizedBox(
                        height: step,
                        width: railWidth,
                        child: Center(
                          child: Text(l, style: TextStyle(fontSize: step < 12 ? 8.5 : 10, fontWeight: FontWeight.w800, height: 1, color: l == lit ? scheme.primary : scheme.onSurfaceVariant)),
                        ),
                      ),
                  ]);
                },
              ),
            ),
            if (_held != null)
              Positioned(
                right: railWidth + 10,
                top: _heldY - 24,
                child: Container(
                  width: 48,
                  height: 48,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(color: scheme.onSurface, borderRadius: BorderRadius.circular(6)),
                  child: Text(_held!, style: TextStyle(color: scheme.surface, fontSize: 24, fontWeight: FontWeight.w800, height: 1)),
                ),
              ),
          ]),
        ),
      );
    });
  }
}
