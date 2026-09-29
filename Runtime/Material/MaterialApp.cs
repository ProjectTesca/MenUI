namespace MenUI.Material3
{
    /// Material Design for an app: `M3` builds the components (see MaterialDesign).
    public class MaterialApp : MenUIApp
    {
        protected MaterialDesign M3 { get { return MaterialDesign.Of(UI); } }
    }
}
