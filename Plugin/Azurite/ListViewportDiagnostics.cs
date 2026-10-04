using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Collections.Generic;
using Studio.Scripts;
using UnityEngine;

namespace Azurite;

internal sealed class ListViewportDiagnostics
{
	private sealed class NestedRow
	{
		private IntPtr _pointer;

		private UIPanel[] _nested = Array.Empty<UIPanel>();

		public int TotalPanelCount { get; private set; }

		public void Discover(ScriptListItem? row)
		{
			IntPtr intPtr = ((row != null) ? row.Pointer : IntPtr.Zero);
			if (_pointer == intPtr)
			{
				return;
			}
			_pointer = intPtr;
			_nested = Array.Empty<UIPanel>();
			TotalPanelCount = 0;
			if (row == null)
			{
				return;
			}
			Il2CppArrayBase<UIPanel> componentsInChildren = row.GetComponentsInChildren<UIPanel>(includeInactive: true);
			TotalPanelCount = componentsInChildren?.Length ?? 0;
			if (componentsInChildren != null)
			{
				_nested = new UIPanel[Math.Min(componentsInChildren.Length, 16)];
				for (int i = 0; i < _nested.Length; i++)
				{
					_nested[i] = componentsInChildren[i];
				}
			}
		}

		public string Summary()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("row=").Append(_pointer.ToString("X")).Append(" panelCount=")
				.Append(TotalPanelCount)
				.Append(" sampledPanels=")
				.Append(_nested.Length);
			UIPanel[] nested = _nested;
			foreach (UIPanel uIPanel in nested)
			{
				if (uIPanel == null)
				{
					stringBuilder.Append("; destroyed-panel");
					continue;
				}
				Il2CppSystem.Collections.Generic.List<UIDrawCall> drawCalls = uIPanel.drawCalls;
				int num = 0;
				int num2 = 0;
				int num3 = 0;
				int num4 = 0;
				int num5 = Math.Min(drawCalls?.Count ?? 0, 128);
				for (int j = 0; j < num5; j++)
				{
					UIDrawCall uIDrawCall = drawCalls[j];
					if (uIDrawCall == null)
					{
						continue;
					}
					if (uIDrawCall.isActiveAndEnabled)
					{
						num++;
					}
					num4 += uIDrawCall.triangles;
					MeshRenderer mRenderer = uIDrawCall.mRenderer;
					if (!(mRenderer == null))
					{
						if (mRenderer.enabled && mRenderer.gameObject != null && mRenderer.gameObject.activeInHierarchy)
						{
							num2++;
						}
						if (mRenderer.isVisible)
						{
							num3++;
						}
					}
				}
				Il2CppSystem.Collections.Generic.List<UIWidget> widgets = uIPanel.widgets;
				int num6 = 0;
				int num7 = Math.Min(widgets?.Count ?? 0, 256);
				for (int k = 0; k < num7; k++)
				{
					UIWidget uIWidget = widgets[k];
					if (uIWidget != null && uIWidget.isVisible)
					{
						num6++;
					}
				}
				stringBuilder.Append("; panel{").Append(Describe(uIPanel)).Append(" active=")
					.Append(uIPanel.isActiveAndEnabled)
					.Append(" cumulativeClip=")
					.Append(uIPanel.hasCumulativeClipping)
					.Append(" sampledDraws=")
					.Append(num5)
					.Append(" enabledDraws=")
					.Append(num)
					.Append(" activeRenderers=")
					.Append(num2)
					.Append(" cameraVisibleRenderers=")
					.Append(num3)
					.Append(" triangles=")
					.Append(num4)
					.Append(" sampledWidgets=")
					.Append(num7)
					.Append(" visibleWidgets=")
					.Append(num6)
					.Append('}');
			}
			return stringBuilder.ToString();
		}
	}

	private const int MaximumRows = 2048;

	private const int MaximumDrawCallsPerPanel = 1024;

	private const double BudgetMilliseconds = 8.0;

	private readonly Action<string> _log;

	private readonly HashSet<IntPtr> _panels = new HashSet<IntPtr>();

	private readonly HashSet<IntPtr> _drawCalls = new HashSet<IntPtr>();

	private double _nextSample;

	private IntPtr _firstRowPointer;

	private int _firstRowNestedPanels = -1;

	private readonly NestedRow _firstNested = new NestedRow();

	private readonly NestedRow _lastNested = new NestedRow();

	private readonly NestedRow _visibleNested = new NestedRow();

	public ListViewportDiagnostics(Action<string> log)
	{
		_log = log ?? throw new ArgumentNullException("log");
	}

	public void Update(double now)
	{
		if (!double.IsFinite(now) || now < 0.0 || now < _nextSample)
		{
			return;
		}
		_nextSample = now + 5.0;
		try
		{
			ScriptNodeInspector instance = ScriptNodeInspector.instance;
			if (instance == null || !instance.isActiveAndEnabled || instance.loading || instance.unloading)
			{
				return;
			}
			CenterableUIScrollView scriptListScroll = instance.scriptListScroll;
			Il2CppSystem.Collections.Generic.List<ScriptListItem> scriptNodeListItemsCache = instance.scriptNodeListItemsCache;
			UIPanel uIPanel = ((scriptListScroll != null && scriptListScroll.isActiveAndEnabled) ? scriptListScroll.panel : null);
			if (uIPanel == null || scriptNodeListItemsCache == null || scriptNodeListItemsCache.Count == 0)
			{
				return;
			}
			long timestamp = Stopwatch.GetTimestamp();
			int count = scriptNodeListItemsCache.Count;
			ScriptListItem row = scriptNodeListItemsCache[0];
			ScriptListItem row2 = scriptNodeListItemsCache[count - 1];
			_firstNested.Discover(row);
			_lastNested.Discover(row2);
			Vector4 finalClipRegion = uIPanel.finalClipRegion;
			Matrix4x4 worldToLocal = uIPanel.worldToLocal;
			bool hasClipping = uIPanel.hasClipping;
			if (!Finite(finalClipRegion.x) || !Finite(finalClipRegion.y) || !Finite(finalClipRegion.z) || !Finite(finalClipRegion.w) || finalClipRegion.z <= 0f || finalClipRegion.w <= 0f)
			{
				_log("list-viewport invalid or empty clipping bounds; visibility unavailable.");
				return;
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			int num7 = 0;
			int num8 = 0;
			bool flag = false;
			string value = "unavailable";
			string value2 = "unknown";
			int num9 = -1;
			_panels.Clear();
			_drawCalls.Clear();
			for (int i = 0; i < Math.Min(count, 2048) && (i == 0 || i % 16 != 0 || !(ElapsedMs(timestamp) >= 8.0)); i++)
			{
				num5++;
				ScriptListItem scriptListItem = scriptNodeListItemsCache[i];
				if (scriptListItem == null)
				{
					num4++;
					continue;
				}
				if (i == 0 && scriptListItem.Pointer != _firstRowPointer)
				{
					_firstRowPointer = scriptListItem.Pointer;
					_firstRowNestedPanels = _firstNested.TotalPanelCount;
				}
				UIWidget child = scriptListItem.child;
				if (child == null)
				{
					num4++;
					continue;
				}
				GameObject gameObject = child.gameObject;
				if (gameObject == null || !gameObject.activeInHierarchy)
				{
					num3++;
					continue;
				}
				UIPanel panel = child.panel;
				if (panel != null && _panels.Add(panel.Pointer))
				{
					if (panel.cullWhileDragging)
					{
						num6++;
					}
					if (panel.alwaysOnScreen)
					{
						num7++;
					}
					num8 += panel.widgets?.Count ?? 0;
					if (_panels.Count == 1)
					{
						value = Describe(panel);
					}
					Il2CppSystem.Collections.Generic.List<UIDrawCall> drawCalls = panel.drawCalls;
					if (drawCalls != null)
					{
						int num10 = Math.Min(drawCalls.Count, 1024);
						flag |= drawCalls.Count > num10;
						for (int j = 0; j < num10; j++)
						{
							UIDrawCall uIDrawCall = drawCalls[j];
							if (uIDrawCall != null)
							{
								_drawCalls.Add(uIDrawCall.Pointer);
							}
						}
					}
				}
				if (!hasClipping)
				{
					num4++;
					continue;
				}
				Il2CppStructArray<Vector3> worldCorners = child.worldCorners;
				if (worldCorners == null || worldCorners.Length < 4)
				{
					num4++;
					continue;
				}
				float num11 = float.PositiveInfinity;
				float num12 = float.PositiveInfinity;
				float num13 = float.NegativeInfinity;
				float num14 = float.NegativeInfinity;
				bool flag2 = true;
				for (int k = 0; k < 4; k++)
				{
					Vector3 vector = worldToLocal.MultiplyPoint3x4(worldCorners[k]);
					if (!Finite(vector.x) || !Finite(vector.y))
					{
						flag2 = false;
						break;
					}
					num11 = Math.Min(num11, vector.x);
					num13 = Math.Max(num13, vector.x);
					num12 = Math.Min(num12, vector.y);
					num14 = Math.Max(num14, vector.y);
				}
				if (!flag2)
				{
					num4++;
				}
				else if (num13 >= finalClipRegion.x - finalClipRegion.z * 0.5f && num11 <= finalClipRegion.x + finalClipRegion.z * 0.5f && num14 >= finalClipRegion.y - finalClipRegion.w * 0.5f && num12 <= finalClipRegion.y + finalClipRegion.w * 0.5f)
				{
					num++;
					if (i == 0)
					{
						value2 = "visible";
					}
					if (num9 < 0)
					{
						num9 = i;
						_visibleNested.Discover(scriptListItem);
					}
				}
				else
				{
					num2++;
					if (i == 0)
					{
						value2 = "outside";
					}
				}
			}
			string value3 = _firstNested.Summary();
			string value4 = _lastNested.Summary();
			string value5 = ((num9 >= 0) ? _visibleNested.Summary() : "not found in sampled rows");
			double value6 = ElapsedMs(timestamp);
			_log($"list-viewport t={now:F3}s rows={count} scanned={num5} partial={num5 != count} boundsVisible={num} boundsOutside={num2} inactive={num3} unknown={num4}; sampledChildOwnerPanels={_panels.Count} ownerDeclaredWidgets={num8} uniqueOwnerDrawCalls={_drawCalls.Count} drawScanTruncated={flag} cullWhileDraggingPanels={num6} alwaysOnScreenPanels={num7}; firstRowNestedPanels={_firstRowNestedPanels}; viewport[{Describe(uIPanel)}] firstChildOwner[{value}]; probeMs={value6:F2}. Bounds are estimates; owner panels may contain unrelated widgets.");
			_log($"list-nested-panels t={now:F3}s firstBounds={value2} first[{value3}]; lastIndex={count - 1} last[{value4}]; firstVisibleIndex={num9} visible[{value5}]. " + "Renderer isVisible is Unity's camera visibility flag, not measured pixels or completed draws.");
		}
		catch (Exception ex)
		{
			_log("list-viewport observation unavailable: " + ex.GetType().Name + ".");
		}
	}

	private static bool Finite(float value)
	{
		return float.IsFinite(value);
	}

	private static double ElapsedMs(long started)
	{
		return (double)(Stopwatch.GetTimestamp() - started) * 1000.0 / (double)Stopwatch.Frequency;
	}

	private static string Describe(UIPanel panel)
	{
		return $"ptr={panel.Pointer.ToString("X")} alpha={panel.alpha:F3} cull={panel.cullWhileDragging} always={panel.alwaysOnScreen} clipping={panel.clipping} widgets={panel.widgets?.Count ?? 0} draws={panel.drawCalls?.Count ?? 0}";
	}
}
