using Grabacr07.KanColleViewer.Composition;
using Grabacr07.KanColleViewer.Plugins.ViewModels;
using Grabacr07.KanColleViewer.Plugins.Views;
using System.ComponentModel.Composition;

namespace Grabacr07.KanColleViewer.Plugins
{
	[Export(typeof(IToolPlugin))]
	[ExportMetadata("Title", "Battle Prediction")]
	[ExportMetadata("Description", "Displays pre-calculated results from a battle.")]
	[ExportMetadata("Version", "1.0")]
	[ExportMetadata("Author", "@FreyYa")]

	public class Preview : IToolPlugin
	{
		private readonly BattlePreviewsViewModel battlePreviewsViewModel = new BattlePreviewsViewModel();
		public string ToolName
		{
			get { return "Battle Prediction"; }
		}

		public object GetSettingsView()
		{
			return null;
		}
		public object GetToolView()
		{
			return new BattlePreviews { DataContext = this.battlePreviewsViewModel };
		}
	}
}
