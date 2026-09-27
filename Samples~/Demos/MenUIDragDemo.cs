using System.Collections.Generic;
using MenUI;
using UnityEngine;

// Drag and drop: a kanban board. Drag a card (hold click, look away) onto a
// column; it lands where the pointer is, reordering within a column too.
// The card being dragged fades (:dragging), the column under it lights up
// (:drag-over), and a preview on the overlay layer follows the pointer.
public class MenUIDragDemo : MenUIApp
{
    // (Instance data: MenSharp shares static fields between all behaviours.)
    private readonly string[] columnNames = { "To do", "Doing", "Done" };

    private Signal<List<int>>[] columns;

    protected override Element Build()
    {
        Color surface = Colors.Hex(0x0F172A);
        Color column = Colors.Hex(0x1E293B);
        Color card = Colors.Hex(0x334155);
        Color accent = Colors.Hex(0x3B82F6);
        Color text = Colors.Hex(0xE2E8F0);

        AddStyleSheet(new StyleSheet()
            .Rule(".column", s => s.Flex().Column().Gap(8).Padding(12).FlexGrow(1).FlexBasis(0).BorderRadius(12)
                .Background(column).Border(2, Colors.Transparent))
            .Rule(".column:drag-over", s => s.BorderColor(accent).Background(Colors.Hex(0x1E3A5F)))
            .Rule(".column .heading", s => s.FontSize(14).TextColor(Colors.Hex(0x94A3B8)))
            .Rule(".card", s => s.Padding(12).BorderRadius(8).Background(card).BoxShadow(Shadow.Small))
            .Rule(".card:hover", s => s.Background(Colors.Hex(0x475569)))
            .Rule(".card:dragging", s => s.Opacity(0.35f))
            .Rule(".preview", s => s.Padding(12).BorderRadius(8).Background(accent).BoxShadow(Shadow.Large)));

        columns = new Signal<List<int>>[columnNames.Length];
        int next = 1;
        for (int c = 0; c < columns.Length; c++)
        {
            List<int> cards = new List<int>();
            for (int k = 0; k < 4 - c; k++)
            {
                cards.Add(next++);
            }
            columns[c] = Signal(cards);
        }

        Box board = Div().Row().Gap(12).FlexGrow(1).AlignItems(Align.Start);
        for (int c = 0; c < columns.Length; c++)
        {
            board.Child(Column(c));
        }
        return Div().Flex().Column().Gap(16).Padding(20).FullSize().Background(surface).TextColor(text).Children(
            Text("Drag cards between columns").FontSize(22),
            board);
    }

    private Box Column(int index)
    {
        Box list = Div().Flex().Column().Gap(8).Children(
            For(() => columns[index].Value, id => id, (id, position) =>
                Div().Class("card")
                    .Draggable(() => id.Value)
                    .DragPreview(() => Div().Class("preview").Children(Text(() => "Task " + id.Value)))
                    .Children(Text(() => "Task " + id.Value))));
        return Div().Class("column")
            .DropTarget((int id, PointerEvent e) => Move(id, index, e.InsertionIndexIn(list)))
            .Children(Text(columnNames[index]).Class("heading"), list);
    }

    /// Moves card `id` to column `to` at `insertAt` (counted with the card
    /// still in place, as the pointer saw the list).
    private void Move(int id, int to, int insertAt)
    {
        int from = -1;
        int oldIndex = -1;
        for (int c = 0; c < columns.Length && from < 0; c++)
        {
            int found = columns[c].Peek().IndexOf(id);
            if (found >= 0)
            {
                from = c;
                oldIndex = found;
            }
        }
        if (from < 0)
        {
            return;
        }
        List<int> source = new List<int>(columns[from].Peek());
        source.RemoveAt(oldIndex);
        List<int> target = from == to ? source : new List<int>(columns[to].Peek());
        if (from == to && oldIndex < insertAt)
        {
            insertAt--;   // the card itself was counted before it
        }
        target.Insert(Mathf.Clamp(insertAt, 0, target.Count), id);
        Batch(() =>
        {
            columns[from].Value = source;
            columns[to].Value = target;
        });
    }
}
