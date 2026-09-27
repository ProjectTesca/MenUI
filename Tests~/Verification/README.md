# Text rendering verification (reference)

The first experiment that proved MenUI's text path: TextMeshPro shapes text in
a hidden TextMeshProUGUI, the glyph quads are read back from
`textInfo.meshInfo`, written to a float record texture, and drawn by one quad
pool draw into a RenderTexture. It lives here (outside the compiled package)
for reference; to run it again, copy the `.cs` files into a project
(`TextRenderingVerification.cs` under a MenSharp source folder, the setup
script under an `Editor` folder) and the shaders anywhere under `Assets`.
