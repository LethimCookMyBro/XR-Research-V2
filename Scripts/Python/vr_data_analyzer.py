#!/usr/bin/env python3
"""
VR Research Data Analyzer
========================
Reads movement_data.csv and interaction_events.json from VR Quest sessions,
calculates metrics, and plots 3D trajectory maps.

Usage:
    python vr_data_analyzer.py --csv movement_data.csv --events interaction_events.json
    python vr_data_analyzer.py --input-dir ./vr_research_data/P001_20260826_143022/
"""

import argparse
import json
import os
import sys
from pathlib import Path
from datetime import datetime

import numpy as np
import pandas as pd
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D
from matplotlib.lines import Line2D


# =============================================================================
# DATA LOADING
# =============================================================================

def load_movement_csv(csv_path: str) -> pd.DataFrame:
    """Load movement_data.csv into a DataFrame."""
    df = pd.read_csv(csv_path)
    print(f"[INFO] Loaded {len(df)} frames from {csv_path}")
    return df


def load_interaction_events(json_path: str) -> list:
    """Load interaction_events.json."""
    if not os.path.exists(json_path):
        print(f"[WARN] Events file not found: {json_path}")
        return []
    with open(json_path, 'r') as f:
        events = json.load(f)
    print(f"[INFO] Loaded {len(events)} events from {json_path}")
    return events


# =============================================================================
# METRICS CALCULATION
# =============================================================================

def calculate_metrics(df: pd.DataFrame, events: list) -> dict:
    """Calculate key research metrics from movement data."""
    metrics = {}

    # Total duration
    metrics['total_duration_s'] = (df['TimestampMs'].iloc[-1] - df['TimestampMs'].iloc[0]) / 1000.0
    metrics['total_frames'] = len(df)
    metrics['avg_fps'] = metrics['total_frames'] / max(metrics['total_duration_s'], 0.001)

    # Head position columns
    head_cols = ['HeadPosX', 'HeadPosY', 'HeadPosZ']
    head_pos = df[head_cols].values

    # Calculate frame-by-frame distances
    diffs = np.diff(head_pos, axis=0)
    frame_distances = np.linalg.norm(diffs, axis=1)

    metrics['total_distance_m'] = float(np.sum(frame_distances))
    metrics['avg_speed_ms'] = float(np.mean(frame_distances)) * metrics['avg_fps']
    metrics['max_speed_ms'] = float(np.max(frame_distances)) * metrics['avg_fps'] if len(frame_distances) > 0 else 0

    # Head movement bounds
    metrics['head_x_range'] = float(np.ptp(head_pos[:, 0]))
    metrics['head_y_range'] = float(np.ptp(head_pos[:, 1]))
    metrics['head_z_range'] = float(np.ptp(head_pos[:, 2]))

    # Controller positions
    if 'LeftCtrlPosX' in df.columns:
        left_ctrl = df[['LeftCtrlPosX', 'LeftCtrlPosY', 'LeftCtrlPosZ']].values
        right_ctrl = df[['RightCtrlPosX', 'RightCtrlPosY', 'RightCtrlPosZ']].values

        left_diffs = np.diff(left_ctrl, axis=0)
        right_diffs = np.diff(right_ctrl, axis=0)

        metrics['left_ctrl_distance_m'] = float(np.sum(np.linalg.norm(left_diffs, axis=1)))
        metrics['right_ctrl_distance_m'] = float(np.sum(np.linalg.norm(right_diffs, axis=1)))

    # Zone time analysis from events
    zone_times = {}
    active_zone = None
    active_enter_time = None

    for event in events:
        event_type = event.get('EventType', '')
        timestamp = event.get('TimestampMs', 0)
        extra = event.get('ExtraData', '{}')

        if isinstance(extra, str):
            try:
                extra_data = json.loads(extra) if extra else {}
            except json.JSONDecodeError:
                extra_data = {}
        else:
            extra_data = extra

        zone_name = extra_data.get('zoneName', 'Unknown')

        if event_type == 'Zone_Enter':
            active_zone = zone_name
            active_enter_time = timestamp
        elif event_type == 'Zone_Exit' and active_zone:
            duration = (timestamp - active_enter_time) / 1000.0
            zone_times[active_zone] = zone_times.get(active_zone, 0) + duration
            active_zone = None
            active_enter_time = None

    metrics['zone_times_s'] = zone_times

    # Interaction summary
    grab_count = sum(1 for e in events if e.get('EventType') == 'Grab')
    release_count = sum(1 for e in events if e.get('EventType') == 'Release')
    teleport_count = sum(1 for e in events if e.get('EventType') == 'Teleport')

    metrics['grab_count'] = grab_count
    metrics['release_count'] = release_count
    metrics['teleport_count'] = teleport_count

    # Average grab duration
    grab_durations = [e.get('Duration', 0) for e in events if e.get('EventType') == 'Release' and e.get('Duration', 0) > 0]
    metrics['avg_grab_duration_s'] = float(np.mean(grab_durations)) / 1000.0 if grab_durations else 0

    return metrics


def print_metrics(metrics: dict):
    """Print metrics in a formatted table."""
    print("\n" + "=" * 60)
    print("VR RESEARCH METRICS REPORT")
    print("=" * 60)

    print(f"\n{'Metric':<35} {'Value':>15}")
    print("-" * 52)
    print(f"{'Total Duration (s)':<35} {metrics['total_duration_s']:>15.2f}")
    print(f"{'Total Frames':<35} {metrics['total_frames']:>15d}")
    print(f"{'Average FPS':<35} {metrics['avg_fps']:>15.1f}")
    print(f"{'Total Distance Traveled (m)':<35} {metrics['total_distance_m']:>15.2f}")
    print(f"{'Average Speed (m/s)':<35} {metrics['avg_speed_ms']:>15.2f}")
    print(f"{'Max Speed (m/s)':<35} {metrics['max_speed_ms']:>15.2f}")

    print(f"\n{'Head Movement Range':<35}")
    print(f"  {'X range (m)':<33} {metrics['head_x_range']:>15.3f}")
    print(f"  {'Y range (m)':<33} {metrics['head_y_range']:>15.3f}")
    print(f"  {'Z range (m)':<33} {metrics['head_z_range']:>15.3f}")

    if 'left_ctrl_distance_m' in metrics:
        print(f"\n{'Controller Distance (m)':<35}")
        print(f"  {'Left':<33} {metrics['left_ctrl_distance_m']:>15.2f}")
        print(f"  {'Right':<33} {metrics['right_ctrl_distance_m']:>15.2f}")

    if metrics['zone_times_s']:
        print(f"\n{'Time Spent in Zones (s)':<35}")
        for zone, time_s in sorted(metrics['zone_times_s'].items()):
            print(f"  {zone:<33} {time_s:>15.2f}")

    print(f"\n{'Interactions':<35}")
    print(f"  {'Grab Count':<33} {metrics['grab_count']:>15d}")
    print(f"  {'Release Count':<33} {metrics['release_count']:>15d}")
    print(f"  {'Teleport Count':<33} {metrics['teleport_count']:>15d}")
    print(f"  {'Avg Grab Duration (s)':<33} {metrics['avg_grab_duration_s']:>15.2f}")

    print("=" * 60)


# =============================================================================
# 3D TRAJECTORY PLOTTING
# =============================================================================

def assign_zones_to_frames(df: pd.DataFrame, events: list) -> np.ndarray:
    """Assign zone labels to each frame based on Zone_Enter/Zone_Exit events."""
    zone_labels = np.full(len(df), 'Unknown', dtype=object)
    current_zone = 'Unknown'

    # Build zone transition timestamps
    zone_events = []
    for event in events:
        event_type = event.get('EventType', '')
        timestamp = event.get('TimestampMs', 0)
        extra = event.get('ExtraData', '{}')

        if isinstance(extra, str):
            try:
                extra_data = json.loads(extra) if extra else {}
            except json.JSONDecodeError:
                extra_data = {}
        else:
            extra_data = extra

        zone_name = extra_data.get('zoneName', 'Unknown')

        if event_type == 'Zone_Enter':
            zone_events.append((timestamp, 'enter', zone_name))
        elif event_type == 'Zone_Exit':
            zone_events.append((timestamp, 'exit', zone_name))

    # Sort events by timestamp
    zone_events.sort(key=lambda x: x[0])

    # Assign zones to frames
    event_idx = 0
    for i, row in df.iterrows():
        ts = row['TimestampMs']

        # Process events up to this timestamp
        while event_idx < len(zone_events) and zone_events[event_idx][0] <= ts:
            _, action, zone_name = zone_events[event_idx]
            if action == 'enter':
                current_zone = zone_name
            elif action == 'exit':
                current_zone = 'Unknown'
            event_idx += 1

        zone_labels[i] = current_zone

    return zone_labels


def plot_3d_trajectory(df: pd.DataFrame, zone_labels: np.ndarray, output_path: str,
                       title: str = "VR Player 3D Trajectory"):
    """Plot 3D trajectory with color-coded zones."""
    fig = plt.figure(figsize=(14, 10))
    ax = fig.add_subplot(111, projection='3d')

    # Get head positions
    x = df['HeadPosX'].values
    y = df['HeadPosY'].values
    z = df['HeadPosZ'].values

    # Get unique zones and assign colors
    unique_zones = list(set(zone_labels))
    zone_colors = {}
    cmap = plt.cm.Set2
    for i, zone in enumerate(sorted(unique_zones)):
        zone_colors[zone] = cmap(i / max(len(unique_zones) - 1, 1))

    # Plot each segment with its zone color
    for zone in unique_zones:
        mask = zone_labels == zone
        color = zone_colors[zone]

        # Find contiguous segments
        indices = np.where(mask)[0]
        if len(indices) == 0:
            continue

        # Split into contiguous segments
        splits = np.split(indices, np.where(np.diff(indices) > 1)[0] + 1)

        for segment in splits:
            if len(segment) < 2:
                ax.scatter(x[segment], y[segment], z[segment],
                          c=[color], s=20, alpha=0.8)
            else:
                ax.plot(x[segment], y[segment], z[segment],
                       c=color, linewidth=1.5, alpha=0.8)

    # Mark start and end
    ax.scatter([x[0]], [y[0]], [z[0]], c='green', s=100, marker='o',
              label='Start', edgecolors='black', linewidth=1.5, zorder=5)
    ax.scatter([x[-1]], [y[-1]], [z[-1]], c='red', s=100, marker='X',
              label='End', edgecolors='black', linewidth=1.5, zorder=5)

    # Labels and formatting
    ax.set_xlabel('X (m)', fontsize=11, labelpad=10)
    ax.set_ylabel('Y (m)', fontsize=11, labelpad=10)
    ax.set_zlabel('Z (m)', fontsize=11, labelpad=10)
    ax.set_title(title, fontsize=14, fontweight='bold', pad=20)

    # Legend
    legend_elements = [
        Line2D([0], [0], color='green', marker='o', markersize=10, label='Start', linewidth=0),
        Line2D([0], [0], color='red', marker='X', markersize=10, label='End', linewidth=0),
    ]
    for zone in sorted(unique_zones):
        if zone != 'Unknown':
            legend_elements.append(
                Line2D([0], [0], color=zone_colors[zone], linewidth=3, label=zone)
            )

    ax.legend(handles=legend_elements, loc='upper left', fontsize=9)

    # Set equal aspect ratio
    max_range = max(
        np.ptp(x), np.ptp(y), np.ptp(z)
    ) / 2.0
    mid_x = np.mean(x)
    mid_y = np.mean(y)
    mid_z = np.mean(z)

    ax.set_xlim(mid_x - max_range, mid_x + max_range)
    ax.set_ylim(mid_y - max_range, mid_y + max_range)
    ax.set_zlim(mid_z - max_range, mid_z + max_range)

    # Style
    ax.view_init(elev=25, azim=45)
    ax.grid(True, alpha=0.3)
    ax.set_facecolor('#f0f0f0')
    fig.patch.set_facecolor('white')

    plt.tight_layout()
    plt.savefig(output_path, dpi=150, bbox_inches='tight')
    plt.close()

    print(f"[INFO] 3D trajectory plot saved to: {output_path}")


def plot_2d_top_view(df: pd.DataFrame, zone_labels: np.ndarray, output_path: str):
    """Plot 2D top-down view of the trajectory."""
    fig, ax = plt.subplots(figsize=(12, 8))

    x = df['HeadPosX'].values
    z = df['HeadPosZ'].values

    unique_zones = list(set(zone_labels))
    zone_colors = {}
    cmap = plt.cm.Set2
    for i, zone in enumerate(sorted(unique_zones)):
        zone_colors[zone] = cmap(i / max(len(unique_zones) - 1, 1))

    for zone in unique_zones:
        mask = zone_labels == zone
        color = zone_colors[zone]
        indices = np.where(mask)[0]
        if len(indices) == 0:
            continue
        splits = np.split(indices, np.where(np.diff(indices) > 1)[0] + 1)
        for segment in splits:
            if len(segment) >= 2:
                ax.plot(x[segment], z[segment], c=color, linewidth=1.5, alpha=0.8)

    ax.scatter([x[0]], [z[0]], c='green', s=100, marker='o', label='Start', zorder=5)
    ax.scatter([x[-1]], [z[-1]], c='red', s=100, marker='X', label='End', zorder=5)

    ax.set_xlabel('X (m)', fontsize=11)
    ax.set_ylabel('Z (m)', fontsize=11)
    ax.set_title('VR Player Trajectory - Top View', fontsize=14, fontweight='bold')
    ax.set_aspect('equal')
    ax.grid(True, alpha=0.3)
    ax.legend(loc='upper left')

    plt.tight_layout()
    plt.savefig(output_path, dpi=150, bbox_inches='tight')
    plt.close()

    print(f"[INFO] 2D top view saved to: {output_path}")


# =============================================================================
# MAIN
# =============================================================================

def main():
    parser = argparse.ArgumentParser(description='VR Research Data Analyzer')
    parser.add_argument('--csv', type=str, help='Path to movement_data.csv')
    parser.add_argument('--events', type=str, help='Path to interaction_events.json')
    parser.add_argument('--input-dir', type=str, help='Directory containing both files')
    parser.add_argument('--output-dir', type=str, default='./output', help='Output directory for plots')
    parser.add_argument('--title', type=str, default='VR Player 3D Trajectory', help='Plot title')
    args = parser.parse_args()

    # Resolve input paths
    if args.input_dir:
        csv_path = os.path.join(args.input_dir, 'movement_data.csv')
        events_path = os.path.join(args.input_dir, 'interaction_events.json')
    elif args.csv:
        csv_path = args.csv
        events_path = args.events or csv_path.replace('movement_data.csv', 'interaction_events.json')
    else:
        print("[ERROR] Provide --csv, --events, or --input-dir")
        sys.exit(1)

    # Create output directory
    os.makedirs(args.output_dir, exist_ok=True)

    # Load data
    print("\n[STEP 1] Loading data...")
    df = load_movement_csv(csv_path)
    events = load_interaction_events(events_path)

    # Calculate metrics
    print("\n[STEP 2] Calculating metrics...")
    metrics = calculate_metrics(df, events)
    print_metrics(metrics)

    # Assign zones to frames
    print("\n[STEP 3] Assigning zones to frames...")
    zone_labels = assign_zones_to_frames(df, events)
    unique_zones, counts = np.unique(zone_labels, return_counts=True)
    for zone, count in zip(unique_zones, counts):
        pct = count / len(zone_labels) * 100
        print(f"  {zone}: {count} frames ({pct:.1f}%)")

    # Plot 3D trajectory
    print("\n[STEP 4] Generating 3D trajectory plot...")
    plot_3d_trajectory(df, zone_labels,
                       os.path.join(args.output_dir, 'trajectory_3d.png'),
                       title=args.title)

    # Plot 2D top view
    print("\n[STEP 5] Generating 2D top view...")
    plot_2d_top_view(df, zone_labels,
                     os.path.join(args.output_dir, 'trajectory_2d_top.png'))

    # Save metrics as JSON
    metrics_path = os.path.join(args.output_dir, 'metrics.json')
    metrics_saveable = {k: v for k, v in metrics.items() if not isinstance(v, (np.ndarray,))}
    with open(metrics_path, 'w') as f:
        json.dump(metrics_saveable, f, indent=2)
    print(f"\n[INFO] Metrics saved to: {metrics_path}")

    print("\n[DONE] Analysis complete!")


if __name__ == '__main__':
    main()
