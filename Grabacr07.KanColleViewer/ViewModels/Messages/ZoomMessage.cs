﻿using Livet.Messaging;
using System.Windows;

namespace Grabacr07.KanColleViewer.ViewModels.Messages
{
	public class ZoomMessage : InteractionMessage
	{
		#region ZoomFactor 依存関係プロパティ

		public int ZoomFactor
		{
			get { return (int)this.GetValue(ZoomFactorProperty); }
			set { this.SetValue(ZoomFactorProperty, value); }
		}
		public static readonly DependencyProperty ZoomFactorProperty =
			DependencyProperty.Register("ZoomFactor", typeof(int), typeof(ZoomMessage), new UIPropertyMetadata(100));

		#endregion

		protected override Freezable CreateInstanceCore()
		{
			return new ZoomMessage
			{
				MessageKey = this.MessageKey,
				ZoomFactor = this.ZoomFactor,
			};
		}
	}
}
