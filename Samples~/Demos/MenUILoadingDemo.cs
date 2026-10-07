using MenUI;
using MenUI.Material3;
using UnityEngine;

// Background loading: a list built a little every frame with async / await
// (a progress bar shows meanwhile), and content that appears only when the
// viewer comes near. Try "sleepDistance" in the inspector too: walk away and
// the panel stops working until you come back.
//
// A panel draws at most 4096 records (about one per box and one per glyph):
// for thousands of rows, use a VirtualList, which only builds what is in view.
public class MenUILoadingDemo : MaterialApp
{
    protected override Element Build()
    {
        ResizePanel(1200, 1200, 1.5f);
        UI.BackgroundBudget = 1.5f;

        // 120 rows, built out of sight: each slice is styled and laid out in
        // its own frame; the list appears when it is complete.
        const int Rows = 120;
        Region list = Deferred(async load =>
        {
            Box column = Div().Column().Gap(2);
            load.Content.Child(column);
            for (int i = 0; i < Rows; i++)
            {
                column.Child(Div().Row().AlignItems(Align.Center).Gap(12).Padding(8, 16).Children(
                    M3.Icon(MaterialSymbol.Article, 20f), M3.Text("Row " + (i + 1), TypeRole.BodyLarge)));
                load.Progress = (i + 1) / (float)Rows;
                await load.Yield();
            }
        }, () => Div().Column().Gap(12).Padding(24).Children(
            M3.Text("Loading " + Rows + " rows…", TypeRole.TitleMedium),
            M3.LinearProgress(null, true)));

        // Near the panel: the full card; farther away: a short line.
        Region near = Near(2.5f,
            () => M3.Card(CardStyle.Elevated).Children(
                M3.Text("You are close", TypeRole.TitleLarge),
                M3.Text(() => "Distance " + UI.ViewerDistance.ToString("0.0") + " m", TypeRole.BodyMedium),
                M3.LoadingIndicator(true)),
            () => M3.Text("Come closer (under 2.5 m) to see more.", TypeRole.BodyMedium));

        return M3.Surface().FullSize().Column().Gap(16).Padding(24).Children(
            M3.Text("Background loading", TypeRole.HeadlineSmall),
            near,
            ScrollArea(ScrollState(), list).FlexGrow(1).MinHeight(0));
    }
}
