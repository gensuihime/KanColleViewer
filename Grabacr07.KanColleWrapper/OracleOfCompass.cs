﻿using Grabacr07.KanColleWrapper.Models;
using Grabacr07.KanColleWrapper.Models.Raw;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Grabacr07.KanColleWrapper
{
	/// <summary>
	/// 전투결과를 미리 받아 그것을 연산합니다.
	/// </summary>
	public class OracleOfCompass
	{
		public int CellData { get; private set; }

		#region private int,List,etc
		/// <summary>
		/// 전투 미리보기 데이터를 저장
		/// </summary>
		private ResultCalLists DataLists { get; set; }
		/// <summary>
		/// 전투 미리보기 리스트
		/// </summary>
		//private List<PreviewBattleResults> Results { get; set; }
		private int RankNum { get; set; }
		private int DockId { get; set; }
		private List<EscapeResults> GoBackPortList { get; set; }
		#endregion

		#region bool
		/// <summary>
		/// 전투 미리보기가 켜져있는가. 켜져있는 경우는 true
		/// </summary>
		public bool EnableBattlePreview { get; set; }
		public bool IsBattleCalculated { get; set; }
		public bool IsCompassCalculated { get; set; }
		/// <summary>
		/// 내부에서 크리티컬이 맞는지 조회하는 부분
		/// </summary>
		public bool IsCritical { get; private set; }
		/// <summary>
		/// 전투가 끝나고 모항에 돌아왔는지를 채크
		/// </summary>
		private bool BattleEnd { get; set; }
		/// <summary>
		/// 연합함대 여부를 저장(수뢰전대 API때문에 넣는 임시 코드)
		/// </summary>
		//public bool Combined { get; set; }
		#endregion

		#region EventHandler
		/// <summary>
		/// 이벤트 핸들러
		/// </summary>
		public delegate void EventHandler();
		/// <summary>
		/// 크리티컬 컨디션 이벤트
		/// </summary>
		public event EventHandler CriticalCondition;
		/// <summary>
		/// 크리티컬 컨디션을 더이상 적용시키지 않기 위해 사용
		/// </summary>
		public event EventHandler CriticalCleared;
		/// <summary>
		/// 크리티컬 컨디션을 미리 알리기 위한 이벤트.
		/// </summary>
		public event EventHandler PreviewCriticalCondition;
		public event EventHandler ReadyForNextCell;
		#endregion

		/// <summary>
		/// 전투결과를 미리 계산합니다. 옵션 설정으로 미리 전투결과를 보거나 보지 않을 수 있습니다.
		/// </summary>
		/// <param name="proxy"></param>
		public OracleOfCompass(KanColleProxy proxy)
		{
			this.DataLists = new ResultCalLists();
			try
			{
				#region 연합함대 전투. airbattle과 일반 연합함대 전투, 수뢰전대 전투가 여기에 포함. 야전은 모두 동일
				proxy.api_req_combined_battle_airbattle.TryParse<kcsapi_battle>().Subscribe(x => this.AirBattle(x.Data));
				proxy.api_req_combined_battle_battle.TryParse<kcsapi_battle>().Subscribe(x => this.Battle(false, false, x.Data, true));
				proxy.api_req_combined_battle_midnight_battle.TryParse<kcsapi_midnight_battle>().Subscribe(x => this.MidBattle(true, false, x.Data, true));
				proxy.api_req_combined_battle_battle_water.TryParse<kcsapi_battle>().Subscribe(x => this.Battle(true, false, x.Data, true));
				#endregion

				#region BattleResult관련. 연합함대와 일반전투만 포함. 연습전 결과는 무시
				proxy.api_req_sortie_battleresult.TryParse().Subscribe(x => this.Result());
				proxy.api_req_combined_battle_battleresult.TryParse<kcsapi_combined_battle_battleresult>().Subscribe(x => this.Result(x.Data));
				#endregion

				#region 연습전(주간,야간)
				proxy.api_req_practice_battle.TryParse<kcsapi_battle>().Subscribe(x => this.Battle(false, true, x.Data, false));
				proxy.api_req_practice_midnight_battle.TryParse<kcsapi_midnight_battle>().Subscribe(x => this.MidBattle(true, true, x.Data, false));
				#endregion
			}
			catch(Exception ex)
			{
				Debug.WriteLine(ex);
			}
			#region 일반 전투. 야전방과 야전->주간전도 여기에 포함
			proxy.api_req_sortie_battle.TryParse<kcsapi_battle>().Subscribe(x => this.Battle(false, false, x.Data, false));
			proxy.api_req_sortie_night_to_day.TryParse<kcsapi_battle>().Subscribe(x => this.Battle(false, false, x.Data, false));
			proxy.api_req_battle_midnight_battle.TryParse<kcsapi_midnight_battle>().Subscribe(x => this.MidBattle(true, false, x.Data, false));
			proxy.api_req_battle_midnight_sp_midnight.TryParse<kcsapi_midnight_battle>().Subscribe(x => this.MidBattle(false, false, x.Data, false));
			#endregion

			#region 변수 초기화 관련
			proxy.api_req_map_start.TryParse<kcsapi_map_start>().Subscribe(x => StartCell(x.Data));
			proxy.api_req_map_next.TryParse<kcsapi_map_next>().Subscribe(x => NextCell(x.Data));
			proxy.api_port.TryParse().Subscribe(x => this.Cleared(true));
			#endregion

		}
		#region 전투미리보기 결과 출력
		/// <summary>
		/// 전투결과를 CriticalPreviewPopup으로 보냅니다.
		/// </summary>
		/// <returns></returns>
		public List<PreviewBattleResults> KanResult(int combinded = -1)
		{
			if (!EnableBattlePreview) return null;
			List<PreviewBattleResults> Results = new List<PreviewBattleResults>();
			var Organization = KanColleClient.Current.Homeport.Organization;

			for (int i = 0; i < 6; i++)
			{
				PreviewBattleResults Kan = new PreviewBattleResults();
				if (combinded == -1)
				{
					if (Organization.Fleets[this.DockId].Ships.Length > i)
					{
						Kan.Name = Organization.Fleets[this.DockId].Ships[i].Name;
						Kan.Lv = Organization.Fleets[this.DockId].Ships[i].Level;
						Kan.CHP = this.DataLists.HpResults[i];
						Kan.MHP = this.DataLists.MHpResults[i];
						Kan.HP = new LimitedValue(this.DataLists.HpResults[i], this.DataLists.MHpResults[i], 0);
						Kan.Status = this.DataLists.CalResults[i];
					}
					if (Kan.HP.Maximum != 0 || Kan.HP.Current != 0) Results.Add(Kan);
				}
				else if (combinded == 1)
				{
					if (Organization.Fleets[1].IsInSortie)
					{
						if (Organization.Fleets[1].Ships.Length > i)
						{
							Kan.Name = Organization.Fleets[1].Ships[i].Name;
							Kan.Lv = Organization.Fleets[1].Ships[i].Level;
							Kan.CHP = this.DataLists.HpResults[i];
							Kan.MHP = this.DataLists.MHpResults[i];
							Kan.HP = new LimitedValue(this.DataLists.HpResults[i], this.DataLists.MHpResults[i], 0);
							Kan.Status = this.DataLists.CalResults[i];
						}
						if (Kan.HP.Maximum != 0 || Kan.HP.Current != 0) Results.Add(Kan);
					}
					else
					{
						for (int j = 0; j < Organization.Fleets.Count; j++)
						{
							if (Organization.Fleets[j].IsInSortie)
							{
								if (Organization.Fleets[j].Ships.Length > i)
								{
									Kan.Name = Organization.Fleets[j].Ships[i].Name;
									Kan.Lv = Organization.Fleets[j].Ships[i].Level;
									Kan.CHP = this.DataLists.HpResults[i];
									Kan.MHP = this.DataLists.MHpResults[i];
									Kan.HP = new LimitedValue(this.DataLists.HpResults[i], this.DataLists.MHpResults[i], 0);
									Kan.Status = this.DataLists.CalResults[i];
								}
								if (Kan.HP.Maximum != 0 || Kan.HP.Current != 0)
								{
									Results.Add(Kan);
									break;
								}
							}
						}
					}
				}

			}
			return Results;
		}
		public List<PreviewBattleResults> SecondResult()
		{
			if (!EnableBattlePreview) return null;
			List<PreviewBattleResults> Results = new List<PreviewBattleResults>();
			var Organization = KanColleClient.Current.Homeport.Organization;

			for (int i = 0; i < 6; i++)
			{
				PreviewBattleResults Kan = new PreviewBattleResults();
				if (Organization.Fleets[2].Ships.Length > i)
				{
					Kan.Name = Organization.Fleets[2].Ships[i].Name;
					Kan.Lv = Organization.Fleets[2].Ships[i].Level;
					Kan.CHP = this.DataLists.ComHpResults[i];
					Kan.MHP = this.DataLists.ComMHpResults[i];
					Kan.HP = new LimitedValue(this.DataLists.ComHpResults[i], this.DataLists.ComMHpResults[i], 0);
					Kan.Status = this.DataLists.ComCalResults[i];
				}
				if (Kan.HP.Maximum != 0 || Kan.HP.Current != 0) Results.Add(Kan);
			}
			return Results;
		}

		public List<PreviewBattleResults> EnemyResult()
		{
			if (!EnableBattlePreview) return null;
			List<PreviewBattleResults> Results = new List<PreviewBattleResults>();
			var ships = KanColleClient.Current.Master.Ships;

			for (int i = 0; i < 6; i++)
			{
				PreviewBattleResults Enemy = new PreviewBattleResults();
				if (this.DataLists.EnemyID.Length > i + 1)
				{
					int temp = this.DataLists.EnemyID[i + 1];
					//심해서함 ID로 이름 찾기
					foreach (var item in ships.Where(x => x.Value.Id == temp).ToArray())
					{
						if (ships.Any(x => x.Value.Id == temp))
						{
							Enemy.Name = item.Value.Name;
							Enemy.Lv = DataLists.EnemyLv[i + 1];
							Enemy.CHP = this.DataLists.EnemyHpResults[i];
							Enemy.MHP = this.DataLists.EnemyMHpResults[i];
							Enemy.HP = new LimitedValue(this.DataLists.EnemyHpResults[i], this.DataLists.EnemyMHpResults[i], 0);
							Enemy.Status = this.DataLists.EnemyCalResults[i];


							if (Enemy.HP.Maximum != 0 || Enemy.HP.Current != 0) Results.Add(Enemy);
						}
					}
				}
			}
			return Results;
		}

		public int RankOut()
		{
			return this.RankNum;
		}
		#endregion

		#region 초기화 및 대파알림
		/// <summary>
		/// 회항하였을때 테마와 악센트를 원래대로
		/// </summary>
		/// <param name="IsEnd">전투가 끝난건지 안 끝난건지의 여부를 입력</param>
		private void Cleared(bool IsEnd)
		{
			BattleClear();

			if (this.IsCritical)
			{
				if (IsEnd)
				{
					this.IsCritical = false;
					this.CriticalCleared();
					this.BattleEnd = true;
					GoBackPortList = new List<EscapeResults>();
				}
				else this.BattleEnd = false;
			}
		}
		private void BattleClear()
		{
			if (EnableBattlePreview)
			{
				DataLists.EnemyDayBattleDamage = 0;
				DataLists.KanDayBattleDamage = 0;
			}
		}
		/// <summary>
		/// battleresult창이 떴을때 IsCritical이 True이면 CriticalCondition이벤트를 발생
		/// 비효율적이지만 매번마다 GoBackPortList를 재작성한다.
		/// </summary>
		/// <param name="result">기본값은 null. 연합함대인경우에만 값을 받아 호위 회항한 부분을 채크</param>
		private void Result(kcsapi_combined_battle_battleresult result = null)
		{
			if (result != null)
			{
				if (result.api_escape_flag == 1)
				{
					if (GoBackPortList == null) GoBackPortList = new List<EscapeResults>();
					else
					{
						GoBackPortList.Clear();
					}
					var escape = result.api_escape.api_escape_idx;
					var tow = result.api_escape.api_tow_idx;

					for (int i = 0; i < escape.Length; i++)
					{
						EscapeResults temp = new EscapeResults
						{
							IsSecond = false,
							escape = escape[i],
						};

						if (temp.escape > 6)//2함대에서 대파가 나는 경우 IsSecond를 true로 하고 escape에서 6을 빼서 저장
						{
							temp.escape = temp.escape - 6;
							temp.IsSecond = true;
						}
						if (tow.Length > i) temp.tow = tow[i] - 6;
						else temp.tow = 20;
						GoBackPortList.Add(temp);
					}
					GoBackPortList.TrimExcess();
				}
			}
			if (this.IsCritical) this.CriticalCondition();
		}
		/// <summary>
		/// 대파알림 계산이 잘못되었거나 문제가 생겼을때를 대비한 코드.
		/// </summary>
		public void AfterResult()
		{
			if (!this.IsCritical)
			{
				if (!this.BattleEnd)
				{
					this.CriticalCondition();
					this.IsCritical = true;
				}
			}
		}
		#endregion

		#region 다음 맵 셀을 확인
		private void StartCell(kcsapi_map_start proxy)
		{
			GoBackPortList = new List<EscapeResults>();

			this.Cleared(false);
			CellData = proxy.api_event_id;
			this.IsCompassCalculated = true;
			this.ReadyForNextCell();
		}
		private void NextCell(kcsapi_map_next proxy)
		{
			this.Cleared(false);
			CellData = proxy.api_event_id;
			this.IsCompassCalculated = true;
			this.ReadyForNextCell();
		}

		#endregion
		/// <summary>
		/// 연합함대를 사용한 전투에서 항공전을 처리하는데 사용.
		/// </summary>
		/// <param name="battle"></param>
		private void AirBattle(kcsapi_battle battle)
		{
			//this.Combined = true;

			this.IsCritical = false;
			List<listup> lists = new List<listup>();
			List<int> CurrentHPList = new List<int>();

			this.ResetEnemyInfo(battle);

			try
			{
				if (battle.api_support_flag != 0) Support(battle.api_support_flag, battle, lists);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex);
			}


			List<listup> Combinelists = new List<listup>();


			//api_kouku시작
			if (battle.api_kouku != null && battle.api_kouku.api_stage3 != null)
			{
				int[] numlist = new int[7];
				decimal[] damlist = battle.api_kouku.api_stage3.api_fdam;

				ChangeKoukuFlagToNumber(
					battle.api_kouku.api_stage3.api_frai_flag,
					battle.api_kouku.api_stage3.api_fbak_flag,
					numlist);
				DecimalListmake(numlist, damlist, lists, true);

				//적에게 준 데미지
				numlist = new int[7];
				damlist = battle.api_kouku.api_stage3.api_edam;

				ChangeKoukuFlagToNumber(
					battle.api_kouku.api_stage3.api_erai_flag,
					battle.api_kouku.api_stage3.api_ebak_flag,
					numlist);
				DecimalListmake(numlist, damlist, lists, false);

				//리스트의 재활용. 여기부터 연합함대 리스트 작성
				if (battle.api_kouku != null && battle.api_kouku.api_stage3_combined != null)
				{
					numlist = new int[7];
					damlist = battle.api_kouku.api_stage3_combined.api_fdam;

					ChangeKoukuFlagToNumber(
						battle.api_kouku.api_stage3_combined.api_frai_flag,
						battle.api_kouku.api_stage3_combined.api_fbak_flag,
						numlist);
					DecimalListmake(numlist, damlist, Combinelists, true);

					//적에게 준 데미지
					numlist = new int[7];
					damlist = battle.api_kouku.api_stage3_combined.api_edam;

					ChangeKoukuFlagToNumber(
						battle.api_kouku.api_stage3_combined.api_erai_flag,
						battle.api_kouku.api_stage3_combined.api_ebak_flag,
						numlist);
					DecimalListmake(numlist, damlist, Combinelists, false);
				}//연합함대 리스트 작성 끝
			}
			//api_kouku끝

			//api_kouku2시작
			if (battle.api_kouku2 != null && battle.api_kouku2.api_stage3 != null)
			{
				int[] numlist = new int[7];
				decimal[] damlist = battle.api_kouku2.api_stage3.api_fdam;

				ChangeKoukuFlagToNumber(battle.api_kouku2.api_stage3.api_frai_flag,
					battle.api_kouku2.api_stage3.api_fbak_flag,
					numlist);
				DecimalListmake(numlist, damlist, lists, true);//메인 함대 리스트 작성 끝

				//적에게 준 데미지
				numlist = new int[7];
				damlist = battle.api_kouku2.api_stage3.api_edam;

				ChangeKoukuFlagToNumber(
					battle.api_kouku2.api_stage3.api_erai_flag,
					battle.api_kouku2.api_stage3.api_ebak_flag,
					numlist);
				DecimalListmake(numlist, damlist, lists, false);

				//리스트의 재활용. 여기부터 연합함대 리스트 작성
				if (battle.api_kouku2 != null && battle.api_kouku2.api_stage3_combined != null)
				{
					numlist = new int[7];
					damlist = battle.api_kouku2.api_stage3_combined.api_fdam;
					for (int i = 0; i < battle.api_kouku2.api_stage3_combined.api_frai_flag.Count(); i++)//컴바인 함대 플래그
					{
						if (
							battle.api_kouku2.api_stage3_combined.api_fbak_flag[i] == 1 ||
							battle.api_kouku2.api_stage3_combined.api_frai_flag[i] == 1)

							numlist[i] = i;
					}
					DecimalListmake(numlist, damlist, Combinelists, true);

					//적에게 준 데미지
					numlist = new int[7];
					damlist = battle.api_kouku2.api_stage3_combined.api_edam;
					for (int i = 0; i < battle.api_kouku2.api_stage3_combined.api_erai_flag.Count(); i++)//컴바인 함대 플래그
					{
						if (
							battle.api_kouku2.api_stage3_combined.api_ebak_flag[i] == 1 ||
							battle.api_kouku2.api_stage3_combined.api_erai_flag[i] == 1)

							numlist[i] = i;
					}
					DecimalListmake(numlist, damlist, Combinelists, false);
				}//연합함대 리스트 작성 끝
			}
			//api_kouku끝
			BattleCalc(lists, CurrentHPList, battle.api_maxhps, battle.api_nowhps, false, false, false, true);

			try
			{
				//적 HP계산을 위해 아군리스트와 적군 리스트를 병합.
				int[] CombinePlusEnemyMaxHPs = new int[13];
				int[] CombinePlusEnemyNowHPs = new int[13];
				//최대 HP병합
				for (int i = 0; i < battle.api_maxhps_combined.Length; i++)
				{
					CombinePlusEnemyMaxHPs[i] = battle.api_maxhps_combined[i];
				}
				for (int i = 7; i < battle.api_maxhps.Length; i++)
				{
					CombinePlusEnemyMaxHPs[i] = battle.api_maxhps[i];
				}

				//현재 HP병합
				for (int i = 0; i < battle.api_nowhps_combined.Length; i++)
				{
					CombinePlusEnemyNowHPs[i] = battle.api_nowhps_combined[i];
				}
				for (int i = 7; i < battle.api_nowhps.Length; i++)
				{
					CombinePlusEnemyNowHPs[i] = CurrentHPList[i];
				}

				//재활용 위해 초기화.
				CurrentHPList = new List<int>();

				BattleCalc(Combinelists, CurrentHPList, CombinePlusEnemyMaxHPs, CombinePlusEnemyNowHPs, true, false, false, true);
				//BattleCalc(HPList, MHPList, Combinelists, CurrentHPList, battle.api_maxhps_combined, battle.api_nowhps_combined,true);
			}
			catch (Exception e)
			{
				Debug.WriteLine(e);
			}


			if (EnableBattlePreview)
			{
				this.PreviewCriticalCondition();
				this.IsBattleCalculated = true;
			}
		}
		/// <summary>
		/// 일반 포격전, 개막뇌격, 개막 항공전등을 계산.
		/// </summary>
		/// <param name="battle"></param>
		/// <param name="Combined">연합함대인경우 True로 설정합니다.</param>
		/// <param name="IsWater">수뢰전대인지 채크합니다</param>
		/// <param name="IsPractice">연습전인지 채크합니다. 연습전인경우 True</param>
		private void Battle(bool IsWater, bool IsPractice, kcsapi_battle battle, bool Combined)
		{
			//this.Combined = IsCombined;
			this.IsCritical = false;
			List<int> CurrentHPList = new List<int>();
			List<listup> lists = new List<listup>();

			this.ResetEnemyInfo(battle);
			try
			{
				if (battle.api_support_flag != 0) Support(battle.api_support_flag, battle, lists);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(ex);
			}
			List<listup> Combinelists = new List<listup>();
			//모든 형태의 전투에서 타게팅이 되는 함선의 번호와 데미지를 순서대로 입력한다.
			//포격전 시작
			if (IsWater)
			{
				if (!Combined && battle.api_hougeki3 != null)
					ObjectListmake(battle.api_hougeki3.api_df_list, battle.api_hougeki3.api_damage, lists);
				else if (Combined && battle.api_hougeki3 != null)//1차 포격전은 2함대만 맞을지도...?
					ObjectListmake(battle.api_hougeki3.api_df_list, battle.api_hougeki3.api_damage, Combinelists);
				if (battle.api_hougeki1 != null)
					ObjectListmake(battle.api_hougeki1.api_df_list, battle.api_hougeki1.api_damage, lists);
			}
			else
			{
				if (!Combined && battle.api_hougeki1 != null)
					ObjectListmake(battle.api_hougeki1.api_df_list, battle.api_hougeki1.api_damage, lists);
				else if (Combined && battle.api_hougeki1 != null)//1차 포격전은 2함대만 맞을지도...?
					ObjectListmake(battle.api_hougeki1.api_df_list, battle.api_hougeki1.api_damage, Combinelists);
				if (battle.api_hougeki3 != null)
					ObjectListmake(battle.api_hougeki3.api_df_list, battle.api_hougeki3.api_damage, lists);
			}
			if (battle.api_hougeki2 != null)
				ObjectListmake(battle.api_hougeki2.api_df_list, battle.api_hougeki2.api_damage, lists);

			//포격전 끝

			//뇌격전 시작
			if (battle.api_raigeki != null)
			{
				int[] numlist = battle.api_raigeki.api_erai;
				decimal[] damlist = battle.api_raigeki.api_eydam;
				if (!Combined)
					DecimalListmake(numlist, damlist, lists, true);
				else
					DecimalListmake(numlist, damlist, Combinelists, true);

				//적 뇌격 데미지
				numlist = battle.api_raigeki.api_frai;
				damlist = battle.api_raigeki.api_fydam;
				if (!Combined)
					DecimalListmake(numlist, damlist, lists, false);
				else
					DecimalListmake(numlist, damlist, Combinelists, false);
			}
			//뇌격전 끝

			//항공전 시작. 폭장과 뇌격 데미지는 합산되어있고 플래그로 맞는 녀석을 선별. ChangeKoukuFlagToNumber에 모든 플래그를 집어넣어서 합산시킴.
			if (battle.api_kouku != null && battle.api_kouku.api_stage3 != null)
			{
				int[] numlist = new int[7];
				decimal[] damlist = battle.api_kouku.api_stage3.api_fdam;

				ChangeKoukuFlagToNumber(battle.api_kouku.api_stage3.api_frai_flag,
					battle.api_kouku.api_stage3.api_fbak_flag,
					numlist);
				DecimalListmake(numlist, damlist, lists, true);

				//적군 데미지
				numlist = new int[7];
				damlist = battle.api_kouku.api_stage3.api_edam;
				ChangeKoukuFlagToNumber(
					battle.api_kouku.api_stage3.api_erai_flag,
					battle.api_kouku.api_stage3.api_ebak_flag,
					numlist);
				DecimalListmake(numlist, damlist, lists, false);

				if (Combined)
				{
					if (battle.api_kouku.api_stage3_combined != null)
					{
						numlist = new int[7];

						damlist = battle.api_kouku.api_stage3_combined.api_fdam;
						ChangeKoukuFlagToNumber(battle.api_kouku.api_stage3_combined.api_frai_flag,
							battle.api_kouku.api_stage3_combined.api_fbak_flag,
							numlist);
						DecimalListmake(numlist, damlist, Combinelists, true);


						//적군피해
						numlist = new int[7];
						if (battle.api_kouku.api_stage3_combined.api_edam != null)
						{
							try
							{
								damlist = battle.api_kouku.api_stage3_combined.api_edam;
								ChangeKoukuFlagToNumber(battle.api_kouku.api_stage3_combined.api_erai_flag,
									battle.api_kouku.api_stage3_combined.api_ebak_flag,
									numlist);
								DecimalListmake(numlist, damlist, Combinelists, false);
							}
							catch (Exception e)
							{
								Debug.WriteLine(e);
							}
						}
					}//연합함대 리스트 작성 끝
				}
			}
			//항공전 끝

			//개막전 시작
			if (battle.api_opening_atack != null)
			{
				int[] numlist = battle.api_opening_atack.api_erai;
				decimal[] damlist = battle.api_opening_atack.api_eydam;
				DecimalListmake(numlist, damlist, lists, true);

				//적 데미지 계산
				numlist = battle.api_opening_atack.api_frai;
				damlist = battle.api_opening_atack.api_fydam;
				DecimalListmake(numlist, damlist, lists, false);
			}
			//개막전 끝
			if (!Combined) BattleCalc(lists, CurrentHPList, battle.api_maxhps, battle.api_nowhps, false, false, IsPractice, Combined);

			else if (Combined)//연합함대인경우 연산을 한번 더 시행
			{
				try
				{
					BattleCalc(lists, CurrentHPList, battle.api_maxhps, battle.api_nowhps, false, false, IsPractice, Combined);

					//적 HP계산을 위해 아군리스트와 적군 리스트를 병합.
					int[] CombinePlusEnemyMaxHPs = new int[13];
					int[] CombinePlusEnemyNowHPs = new int[13];
					//최대 HP병합
					for (int i = 0; i < battle.api_maxhps_combined.Length; i++)
					{
						CombinePlusEnemyMaxHPs[i] = battle.api_maxhps_combined[i];
					}
					for (int i = 7; i < battle.api_maxhps.Length; i++)
					{
						CombinePlusEnemyMaxHPs[i] = battle.api_maxhps[i];
					}

					//현재 HP병합
					for (int i = 0; i < battle.api_nowhps_combined.Length; i++)
					{
						CombinePlusEnemyNowHPs[i] = battle.api_nowhps_combined[i];
					}
					for (int i = 7; i < battle.api_nowhps.Length; i++)
					{
						CombinePlusEnemyNowHPs[i] = CurrentHPList[i];
					}
					//재활용 위해 초기화.
					CurrentHPList = new List<int>();

					BattleCalc(Combinelists, CurrentHPList, CombinePlusEnemyMaxHPs, CombinePlusEnemyNowHPs, true, false, IsPractice, Combined);
				}
				catch (Exception e)
				{
					Debug.WriteLine(e);
				}


			}
			if (EnableBattlePreview)
			{
				this.PreviewCriticalCondition();
				this.IsBattleCalculated = true;
			}
		}
		/// <summary>
		/// battle의 야전버전. Battle과 구조는 동일. 다만 전투의 양상이 조금 다르기때문에 분리. 연합함대와 아닌경우의 구분을 한다.
		/// </summary>
		/// <param name="battle"></param>
		/// <param name="IsMidnight">주간전이 있었던 야전인경우 true</param>
		/// <param name="IsCombined">연합함대인지 아닌지 입력합니다.연합함대인경우 True입니다.</param>
		/// <param name="IsPractice">연습전인경우 채크합니다. 연습전이면 True</param>
		private void MidBattle(bool IsMidnight, bool IsPractice, kcsapi_midnight_battle battle, bool Combined)
		{
			if (!Combined) this.IsCritical = false;
			//this.Combined = IsCombined;
			List<listup> lists = new List<listup>();
			List<int> CurrentHPList = new List<int>();

			this.ResetEnemyInfo(battle);

			//포격전 리스트를 작성. 주간과 달리 1차 포격전밖에 없음.
			if (battle.api_hougeki != null)
			{
				ObjectListmake(battle.api_hougeki.api_df_list, battle.api_hougeki.api_damage, lists);

				if (Combined && battle.api_maxhps_combined != null && battle.api_nowhps_combined != null)
				{
					try
					{
						//현재 이 부분은 API가 정확히 기억이 나지 않기때문에 일단은 이렇게 처리.
						//적 HP계산을 위해 아군리스트와 적군 리스트를 병합.
						int[] CombinePlusEnemyMaxHPs = new int[13];
						int[] CombinePlusEnemyNowHPs = new int[13];
						//최대 HP병합
						for (int i = 0; i < battle.api_maxhps_combined.Length; i++)
						{
							CombinePlusEnemyMaxHPs[i] = battle.api_maxhps_combined[i];
						}
						for (int i = 7; i < battle.api_maxhps.Length; i++)
						{
							CombinePlusEnemyMaxHPs[i] = battle.api_maxhps[i];
						}

						//현재 HP병합
						for (int i = 0; i < battle.api_nowhps_combined.Length; i++)
						{
							CombinePlusEnemyNowHPs[i] = battle.api_nowhps_combined[i];
						}
						for (int i = 7; i < battle.api_nowhps.Length; i++)
						{
							CombinePlusEnemyNowHPs[i] = battle.api_nowhps[i];
						}

						BattleCalc(lists, CurrentHPList, CombinePlusEnemyMaxHPs, CombinePlusEnemyNowHPs, true, IsMidnight, IsPractice, Combined);
					}
					catch (Exception e)
					{
						Debug.WriteLine(e);
					}

				}
				else
					BattleCalc(lists, CurrentHPList, battle.api_maxhps, battle.api_nowhps, false, IsMidnight, IsPractice, Combined);
			}
			if (EnableBattlePreview)
			{
				this.PreviewCriticalCondition();
				this.IsBattleCalculated = true;
			}
		}

		/// <summary>
		/// 전투결과 계산의 총계를 낸다.
		/// </summary>
		/// <param name="lists">데미지를 모두 저장한 리스트를 입력.</param>
		/// <param name="CurrentHPList">계산이 끝난 HP리스트를 적제한다.</param>
		/// <param name="Maxhps">api_maxhps를 가져온다.</param>
		/// <param name="NowHps">api_nowhps를 가져온다.</param>
		/// <param name="IsCombined">이 계산은 연합함대 대상으로 계산되는가?</param>
		/// <param name="IsMidnight">주간전이 있었던 야전이면 True</param>
		/// <param name="IsPractice">연습전이면 True</param>
		/// <param name="Combined">현재 연합함대가 결성되어있는가</param>
		private void BattleCalc(List<listup> lists, List<int> CurrentHPList, int[] Maxhps, int[] NowHps, bool IsCombined, bool IsMidnight, bool IsPractice, bool Combined)
		{
			#region 전투 미리보기 관련값 초기화
			if (EnableBattlePreview)
			{

				DataLists.ComCalResults.Clear();
				DataLists.ComCalResults.TrimExcess();
				DataLists.ComHpResults.Clear();
				DataLists.ComHpResults.TrimExcess();
				DataLists.ComMHpResults.Clear();
				DataLists.ComMHpResults.TrimExcess();


				DataLists.EnemyCalResults.Clear();
				DataLists.EnemyHpResults.Clear();
				DataLists.EnemyMHpResults.Clear();
				DataLists.EnemyCalResults.TrimExcess();
				DataLists.EnemyHpResults.TrimExcess();
				DataLists.EnemyMHpResults.TrimExcess();

				DataLists.IsEnemyFlagDead = false;
				if (!IsCombined)
				{
					DataLists.CalResults.Clear();
					DataLists.HpResults.Clear();
					DataLists.MHpResults.Clear();
					DataLists.CalResults.TrimExcess();
					DataLists.HpResults.TrimExcess();
					DataLists.MHpResults.TrimExcess();

					if (!IsMidnight) DataLists.IsKanDamaged = false;
					DataLists.IsOverDamage = false;
					DataLists.IsMidDamage = false;
					DataLists.IsScratch = false;
				}
			}
			int KanCount = 0;
			int KanDeadCount = 0;
			int EnemyCount = 0;
			int EnemyDeadCount = 0;
			#endregion

			//데미지를 계산하여 현재 HP에 적용
			for (int i = 0; i < NowHps.Length; i++)
			{
				int MaxHP = Maxhps[i];
				int CurrentHP = NowHps[i];

				for (int j = 0; j < lists.Count; j++)
				{
					decimal rounded = decimal.Round(lists[j].Damage);
					int damage = (int)rounded;
					if (lists[j].Num == i) CurrentHP = CurrentHP - damage;
				}
				CurrentHPList.Add(CurrentHP);
			}
			//25%보다 적은지 많은지 판별
			List<bool> result = new List<bool>();
			int EnemyEveryMHP = 0, EnemyEveryCHP = 0, KanEveryMHP = 0, KanEveryCHP = 0;

			for (int i = 0; i < CurrentHPList.Count; i++)
			{
				if (Maxhps[i] != -1)
				{
					double temp = (double)CurrentHPList[i] / (double)Maxhps[i];
					if (!IsPractice)
					{
						bool escape = false;
						bool tow = false;

						if (GoBackPortList != null && GoBackPortList.Count > 0)
						{
							if (!IsCombined)
							{
								var firstlist = GoBackPortList.Where(x => !x.IsSecond);
								if (firstlist.Count() > 0)
									escape = firstlist.Any(x => x.escape == i + 1);
							}
							else
							{
								var secondlist = GoBackPortList.Where(x => x.IsSecond);
								if (secondlist.Count() > 0)
									escape = secondlist.Any(x => x.escape == i + 1);
								tow = GoBackPortList.Any(x => x.tow == i + 1);
							}
						}
						if (escape) result.Add(false);
						else if (tow) result.Add(false);
						else if (temp <= 0.25) result.Add(true);
						else result.Add(false);
					}

					//이하부터 전투 미리보기 시작.
					#region 전투 미리보기
					if (EnableBattlePreview)
					{
						if (CurrentHPList[i] < 0) CurrentHPList[i] = 0;
						//HP정보를 리스트에 저장

						//연합함대 수정필요
						if (i < 7)//아군정보
						{
							bool escape = false;
							bool tow = false;
							if (IsCombined)
							{
								DataLists.ComMHpResults.Add(Maxhps[i]);
								DataLists.ComHpResults.Add(CurrentHPList[i]);
								if (GoBackPortList != null && GoBackPortList.Count > 0)
								{

									var secondlist = GoBackPortList.Where(x => x.IsSecond);
									if (secondlist.Count() > 0)
										escape = secondlist.Any(x => x.escape == i);
									tow = GoBackPortList.Any(x => x.tow == i);
								}
								if (escape || tow) DataLists.ComCalResults.Add(5);//회항
								else if (temp <= 0) DataLists.ComCalResults.Add(4);//격침
								else if (temp <= 0.25) DataLists.ComCalResults.Add(3);//대파
								else if (temp <= 0.5) DataLists.ComCalResults.Add(2);//중파
								else if (temp <= 0.75) DataLists.ComCalResults.Add(1);//소파
								else DataLists.ComCalResults.Add(0);//통상
							}
							else
							{
								DataLists.MHpResults.Add(Maxhps[i]);
								DataLists.HpResults.Add(CurrentHPList[i]);
								if (GoBackPortList != null && GoBackPortList.Count > 0)
								{
									var firstlist = GoBackPortList.Where(x => !x.IsSecond);
									if (firstlist.Count() > 0)
										escape = firstlist.Any(x => x.escape == i);
								}
								if (escape || tow) DataLists.CalResults.Add(5);//회항
								else if (temp <= 0) DataLists.CalResults.Add(4);//격침
								else if (temp <= 0.25) DataLists.CalResults.Add(3);//대파
								else if (temp <= 0.5) DataLists.CalResults.Add(2);//중파
								else if (temp <= 0.75) DataLists.CalResults.Add(1);//소파
								else DataLists.CalResults.Add(0);//통상


							}

							KanEveryCHP = KanEveryCHP + (NowHps[i] - CurrentHPList[i]);
							KanEveryMHP = KanEveryMHP + NowHps[i];

							KanCount++;
							if (CurrentHPList[i] == 0) KanDeadCount++;
						}
						else//적군정보
						{
							DataLists.EnemyMHpResults.Add(Maxhps[i]);
							DataLists.EnemyHpResults.Add(CurrentHPList[i]);

							if (temp <= 0) DataLists.EnemyCalResults.Add(4);//격침
							else if (temp <= 0.25) DataLists.EnemyCalResults.Add(3);//대파
							else if (temp <= 0.5) DataLists.EnemyCalResults.Add(2);//중파
							else if (temp <= 0.75) DataLists.EnemyCalResults.Add(1);//소파
							else DataLists.EnemyCalResults.Add(0);//통상

							EnemyEveryCHP = EnemyEveryCHP + (NowHps[i] - CurrentHPList[i]);
							EnemyEveryMHP = EnemyEveryMHP + NowHps[i];

							if (NowHps[i] - CurrentHPList[i] > 0 && !DataLists.IsEnemyDamaged) DataLists.IsEnemyDamaged = true;

							EnemyCount++;
							if (CurrentHPList[i] <= 0) EnemyDeadCount++;
							if (CurrentHPList[7] <= 0) DataLists.IsEnemyFlagDead = true;
						}
					}
					#endregion
				}
				else if (!IsPractice) result.Add(false);
			}
			//이하 랭크 예측관련
			#region 랭크예측
			if (EnableBattlePreview)
			{
				if (Combined && !IsCombined)
				{
					this.DataLists.FirstKanDamaged = KanEveryCHP;
					this.DataLists.FirstKanMaxHP = KanEveryMHP;
				}
				else if (Combined && IsCombined && !IsMidnight)
				{
					KanEveryCHP = KanEveryCHP + this.DataLists.FirstKanDamaged;
					KanEveryMHP = KanEveryMHP + this.DataLists.FirstKanMaxHP;
				}
				decimal EnemyDamage = (decimal)EnemyEveryCHP / (decimal)EnemyEveryMHP;
				decimal KanDamage = (decimal)KanEveryCHP / (decimal)KanEveryMHP;
				if (IsMidnight)
				{
					EnemyDamage = (decimal)(EnemyEveryCHP + DataLists.EnemyDayBattleDamage) / (decimal)(EnemyEveryMHP + DataLists.EnemyDayBattleDamage);
					KanDamage = (decimal)(KanEveryCHP + DataLists.KanDayBattleDamage) / (decimal)(KanEveryMHP + DataLists.KanDayBattleDamage);
				}
				else
				{
					DataLists.EnemyDayBattleDamage = EnemyEveryCHP;
					DataLists.KanDayBattleDamage = KanEveryCHP;
				}
				//flag조작 시작


				if (KanDamage != 0)
				{
					decimal CalcPercent = EnemyDamage / KanDamage;
					if (CalcPercent > 2.5m)
						DataLists.IsOverDamage = true;//2.5배 초과 데미지
					else if (CalcPercent >= 1)
						DataLists.IsMidDamage = true;//1초과 2.5이하
					else DataLists.IsScratch = true;//1미만
				}
				else if (KanDamage == 0 && EnemyDamage == 0) DataLists.IsScratch = true;
				else if (KanDamage == 0) DataLists.IsOverDamage = true;//아군피해 0인 경우

				if (KanDamage == 0) DataLists.IsKanDamaged = false;
				else DataLists.IsKanDamaged = true;

				if (DataLists.IsEnemyDamaged) if (EnemyDamage <= 0.001m && EnemyDamage > 0) DataLists.IsEnemyDamaged = false;

				if (EnemyEveryMHP - EnemyEveryCHP <= 0) DataLists.IsEnemyExterminated = true;//적 전멸
				else DataLists.IsEnemyExterminated = false;

				if (KanDeadCount > 0) DataLists.IsKanDead = true;//아군격침여부
				else DataLists.IsKanDead = false;

				if (KanDeadCount > 0 && KanDeadCount < EnemyDeadCount)
					DataLists.IsEnemyDeadOver = true;//적격침이 아군격침보다 많음
				else DataLists.IsEnemyDeadOver = false;

				if (EnemyCount == 4 || EnemyCount == 2)//적의 수가 2나 4일 경우엔 0.5이상 이외에는 0.5초과 1인경우는 예외
				{
					if ((double)EnemyDeadCount / (double)EnemyCount >= 0.5)
						DataLists.IsEnemyDeadOverHalf = true;
					else DataLists.IsEnemyDeadOverHalf = false;
				}
				else if (EnemyCount > 1 && (double)EnemyDeadCount / (double)EnemyCount > 0.5) DataLists.IsEnemyDeadOverHalf = true;
				else DataLists.IsEnemyDeadOverHalf = false;
				//스위치 조작 종료

				//랭크 연산 적용
				try
				{
					this.RankNum = this.RankCalc();
				}
				catch (Exception e)
				{
					this.RankNum = -1;
					System.Diagnostics.Debug.WriteLine(e);
				}
			}
			#endregion

			if (!IsPractice)
			{
				for (int i = 0; i < result.Count; i++)
				{
					if (result[i] && i < 7)
					{
						this.IsCritical = true;
						break;
					}
				}
			}
		}

		//리스트 작성부분은 좀 더 매끄러운 방법이 있는 것 같기도 하지만 일단 능력이 여기까지이므로
		#region 데미지 리스트 작성부분
		/// <summary>
		/// decimal 데미지 리스트를 생성. 포격전이나 기타 컷인전투와 달리 Decimal값으로 바로 나온다.
		/// </summary>
		/// <param name="ho_target">타격대상 리스트를 입력합니다.</param>
		/// <param name="ho_damage">decimal[]형태의 데미지를 입력합니다.</param>
		/// <param name="list">출력할 데미지 리스트를 입력합니다.</param>
		/// <param name="Friendly">피아를 입력합니다. 아군인경우나 구분이 필요없는경우는 true, 적군인경우 false를 입력</param>
		private void DecimalListmake(int[] target, decimal[] damage, List<listup> list, bool Friendly)
		{
			for (int i = 1; i < target.Count(); i++)//-1제외
			{
				int j = (Friendly == true) ? 0 : 6;

				listup d = new listup
				{
					Num = target[i] + j,
					Damage = damage[i]
				};
				if (d.Num != 0) list.Add(d);
			}

		}
		/// <summary>
		/// object 데미지 리스트를 생성. object로 포장되어 있어 이걸 분해하여 리스트로 넣어줍니다.
		/// </summary>
		/// <param name="target">타겟 리스트</param>
		/// <param name="damage">데미지 리스트. 한 리스트에 object로 0~2까지 포장되어있음. 보통은 1까지만 쓰이는 모양</param>
		/// <param name="list"></param>
		private void ObjectListmake(object[] target, object[] damage, List<listup> list)
		{
			for (int i = 1; i < target.Count(); i++)//-1제외
			{
				dynamic listNum = target[i];
				dynamic damNum = damage[i];
				listup d = new listup
				{
					Num = listNum[0],
					Damage = damNum[0]
				};
				//혹시 모르는 부분을 대비해서 별도 추가. 한 공격자의 타겟이 여러개인 부분을 상정. 일반적으로 타겟은 일정하기때문에 동일타겟이 연속으로 데미지를 입는다.
				if (listNum.Length > 1 && damNum.Length > 1 && listNum.Length == damNum.Length)
				{
					for (int j = 0; j < listNum.Length; j++)
					{
						listup e = new listup();
						e.Num = listNum[j];
						e.Damage = damNum[j];
						list.Add(e);
					}
				}
				else list.Add(d);
			}

		}
		/// <summary>
		/// 항공전 Flag를 순번으로 바꿔줍니다. flag가 0이면 0으로 바꿉니다.
		/// </summary>
		/// <param name="rFlag">뇌격 flag입니다</param>
		/// <param name="bFlag">폭격 flag입니다</param>
		/// <param name="numlist">순번을 넣을 번호 리스트입니다.</param>
		private void ChangeKoukuFlagToNumber(int[] rFlag, int[] bFlag, int[] numlist)
		{
			for (int i = 0; i < rFlag.Count(); i++)//플래그를 번호로 변환
			{
				if (rFlag[i] == 1 || bFlag[i] == 1) numlist[i] = i;
				else numlist[i] = 0;
			}
		}
		#endregion

		#region 전투 미리보기 관련
		/// <summary>
		/// 랭크를 계산합니다.
		/// 0=완전승리	1=S승	2=A승	3=B승	4=C패배		5=D패배		-1=예측불능
		/// </summary>
		private int RankCalc()
		{
			if (!DataLists.IsEnemyDamaged && !DataLists.IsKanDamaged) return 5;
			else if (DataLists.IsKanDead)
			{
				if (DataLists.IsEnemyFlagDead)
				{
					if (DataLists.IsEnemyDeadOver) return 3;
					else if (!DataLists.IsEnemyDeadOver) return 4;
					return -1;//예측불능
				}
				else if (!DataLists.IsEnemyFlagDead)
				{
					if (DataLists.IsMidDamage) return 4;
					return -1;//예측불능
				}
				else return -1;//예측불능
			}
			else if (!DataLists.IsKanDead)
			{
				if (DataLists.IsEnemyFlagDead)
				{
					if (DataLists.IsEnemyExterminated)
					{
						if (DataLists.IsKanDamaged) return 1;
						else if (!DataLists.IsKanDamaged) return 0;
					}
					else if (!DataLists.IsEnemyExterminated)
					{
						if (DataLists.IsEnemyDeadOverHalf) return 2;
						else if (!DataLists.IsEnemyDeadOverHalf) return 3;
					}
					return -1;//예측불능
				}
				else if (!DataLists.IsEnemyFlagDead)
				{
					if (DataLists.IsEnemyDeadOverHalf) return 2;

					if (DataLists.IsOverDamage) return 3;

					else if (DataLists.IsMidDamage) return 4;
					else if (DataLists.IsScratch) return 5;
					else return -1;//예측불능
				}
				return -1;//예측불능

			}
			else return -1;//예측불능
		}
		/// <summary>
		/// 지원함대 데미지를 계산합니다.
		/// </summary>
		/// <param name="SupportFlag">지원함대 플래그를 입력합니다. 0:지원x 1:항공	2:포격	3:뇌격</param>
		/// <param name="battle"></param>
		/// <param name="lists"></param>
		private void Support(int SupportFlag, kcsapi_battle battle, List<listup> lists)
		{
			if (battle.api_support_flag == 0) return;
			if (battle.api_support_flag == 1)//항공지원
			{
				decimal[] Damage;

				if (battle.api_support_info.api_support_airatack == null) return;

				Damage = battle.api_support_info.api_support_airatack.api_stage3.api_edam;
				int[] Numlist = new int[Damage.Length];

				ChangeKoukuFlagToNumber(
					battle.api_support_info.api_support_airatack.api_stage3.api_erai_flag,
					battle.api_support_info.api_support_airatack.api_stage3.api_ebak_flag,
					Numlist);
				DecimalListmake(Numlist, Damage, lists, false);
			}
			else if (battle.api_support_flag == 2 || battle.api_support_flag == 3)//포격,뇌격지원. 포격:2 뇌격:3
			{
				decimal[] Damage;

				if (battle.api_support_info.api_support_hourai == null) return;

				Damage = battle.api_support_info.api_support_hourai.api_damage;
				int[] Numlist = new int[Damage.Length];

				for (int i = 0; i < Damage.Length; i++)
				{
					if (Damage[i] > 0) Numlist[i] = i;
					else Numlist[i] = 0;
				}

				DecimalListmake(Numlist, Damage, lists, false);
			}
		}
		private void ResetEnemyInfo(kcsapi_battle battle)
		{
			if (EnableBattlePreview)
			{
				int i = battle.api_ship_ke.Count();
				DataLists.EnemyID = new int[i];
				DataLists.EnemyID = battle.api_ship_ke;
				i = battle.api_ship_lv.Count();
				DataLists.EnemyLv = new int[i];
				DataLists.EnemyLv = battle.api_ship_lv;

				this.DockId = battle.api_dock_id;
			}
		}
		private void ResetEnemyInfo(kcsapi_midnight_battle battle)
		{
			if (EnableBattlePreview)
			{
				int i = battle.api_ship_ke.Count();
				DataLists.EnemyID = new int[i];
				DataLists.EnemyID = battle.api_ship_ke;
				i = battle.api_ship_lv.Count();
				DataLists.EnemyLv = new int[i];
				DataLists.EnemyLv = battle.api_ship_lv;

				this.DockId = battle.api_deck_id;
			}
		}
		#endregion

	}
}