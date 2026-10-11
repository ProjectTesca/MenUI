using MenSharp;
using MenUI;
using UnityEngine;
using VRC.SDKBase;

// Images embedded in the world: assign textures in the inspector (any size;
// a large photo shown small is copied small). The buttons switch how the
// photo fills its box, tint the icon, swap the photo for the icon (a
// reactive image) and show a grid of copies at many sizes. Web images: set
// URLs in `imageUrls` (PNG or JPEG on a trusted domain, e.g. GitHub or Imgur);
// VRChat downloads one every 5 seconds.
public class MenUIImageDemo : MenUIApp
{
    public Texture2D photo;
    public Texture2D icon;
    // A sheet of sprites; `spriteSize` px squares, read from its top-left.
    public Texture2D sheet;
    public int spriteSize = 64;
    public VRCUrl[] imageUrls;

    private Signal<string> webStatus;

    protected override Element Build()
    {
        SetTheme(Theme.Dark);
        Color surface = Colors.Hex(0x1E293B);
        Color raised = Colors.Hex(0x334155);
        Color accent = Colors.Hex(0x3B82F6);

        AddStyleSheet(new StyleSheet()
            .Rule(".button", s => s.Center().Padding(8, 16).BorderRadius(8).Background(raised))
            .Rule(".button:hover", s => s.Background(Colors.Hex(0x475569)))
            .Rule(".frame", s => s.Background(Colors.Hex(0x0F172A)).Border(1, raised))
            .Rule(".caption", s => s.FontSize(13).TextColor(Colors.Hex(0x94A3B8))));

        Signal<int> fitIndex = Signal(0);
        Signal<bool> tinted = Signal(false);
        Signal<bool> swapped = Signal(false);
        Signal<bool> grid = Signal(false);
        webStatus = Signal("");
        ImageFit[] fits = { ImageFit.Fill, ImageFit.Contain, ImageFit.Cover, ImageFit.None, ImageFit.ScaleDown };

        Box page = Div().Column().Gap(16).Padding(24).Children(
            Text("Images").FontSize(28),
            photo == null && icon == null && sheet == null
                ? Text("Assign photo, icon and sheet on this behaviour to see them.").Class("caption")
                : null,

            // Natural size, a set width (the height follows), and fits.
            Div().Row().Gap(16).AlignItems(Align.Start).Children(
                Div().Column().Gap(4).Children(
                    Image(photo).Width(160),
                    Text("Width(160)").Class("caption")),
                Div().Column().Gap(4).Children(
                    Dynamic(() =>
                    {
                        ImageFit fit = fits[fitIndex.Value];
                        return Image(photo).Size(160, 160).Fit(fit).Class("frame");
                    }),
                    Text(() => "Fit." + fits[fitIndex.Value]).Class("caption")),
                Div().Column().Gap(4).Children(
                    Image(photo).Size(96, 96).Fit(ImageFit.Cover).BorderRadius(Length.Percent(50)),
                    Text("Cover, round").Class("caption")),
                Div().Column().Gap(4).Children(
                    Image(() => swapped.Value ? icon : photo).Size(96, 96).Fit(ImageFit.Contain).Class("frame"),
                    Text("Image(() => ...)").Class("caption"))),

            // An icon at its natural size and tinted; sprites from a sheet.
            Div().Row().Gap(16).AlignItems(Align.Center).Children(
                Image(icon),
                Image(icon).Size(48, 48).Fit(ImageFit.Contain).Tint(accent),
                Image(sheet, new Rect(0, 0, spriteSize, spriteSize)).Size(48, 48),
                Image(sheet, new Rect(spriteSize, 0, spriteSize, spriteSize)).Size(48, 48)),

            Div().Row().Gap(8).Children(
                Div().Class("button").OnClick(() => fitIndex.Value = (fitIndex.Peek() + 1) % fits.Length)
                    .Children(Text("Next fit")),
                Div().Class("button").OnClick(() => swapped.Value = !swapped.Peek())
                    .Children(Text("Swap image")),
                Div().Class("button").OnClick(() => grid.Value = !grid.Peek())
                    .Children(Text(() => grid.Value ? "Hide grid" : "Show grid"))),

            // From the web: a placeholder while it comes, a message if it fails.
            Text("From the web").FontSize(20),
            WebRow(raised),
            Text(() => webStatus.Value).Class("caption"),

            // Many copies at many sizes (the atlas packs them; hiding the
            // grid lets them go, and they are dropped when room is needed).
            Show(() => grid.Value, () =>
            {
                Box cells = Div().Row().FlexWrap(MenUI.WrapMode.Wrap).Gap(8);
                for (int i = 0; i < 24; i++)
                {
                    cells.Child(Image(i % 2 == 0 ? photo : icon).Size(24 + i * 6, 24 + i * 6).Fit(ImageFit.Cover)
                        .BorderRadius(6));
                }
                return cells;
            }));
        return ScrollArea(ScrollState(), page).FullSize().Background(surface).TextColor(Colors.Hex(0xE2E8F0));
    }

    private Box WebRow(Color raised)
    {
        Box row = Div().Row().FlexWrap(MenUI.WrapMode.Wrap).Gap(12);
        if (imageUrls == null || imageUrls.Length == 0)
        {
            return row.Child(Text("Set URLs in imageUrls to see downloads.").Class("caption"));
        }
        for (int i = 0; i < imageUrls.Length; i++)
        {
            ImageSource image = LoadImage(imageUrls[i]);
            row.Child(Show(() => image.IsLoaded,
                () => Image(image).Size(160, 120).Fit(ImageFit.Cover).BorderRadius(8),
                () => Div().Size(160, 120).BorderRadius(8).Background(raised).Center().Children(
                    Text(() => image.HasFailed ? image.Error.Kind.ToString() : "Loading...").Class("caption"))));
        }
        return row;
    }

    // async/await: the first web image's outcome, as a value.
    protected override async void OnMounted()
    {
        if (imageUrls == null || imageUrls.Length == 0)
        {
            return;
        }
        Result<ImageSource, ImageError> loaded = await LoadImageAsync(imageUrls[0]);
        loaded.Switch(
            image => webStatus.Value = "First image: " + image.Width + " x " + image.Height + " px",
            error => webStatus.Value = "First image failed: " + error);
    }
}
