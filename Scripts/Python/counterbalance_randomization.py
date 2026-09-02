#!/usr/bin/env python3
"""
VR Research Counterbalance Randomization
=========================================
Scans existing session folders to determine current A/B balance,
then suggests or assigns the next participant's task order.

Usage:
    python counterbalance_randomization.py --quest-data-dir ./vr_research_data/
    python counterbalance_randomization.py --quest-data-dir ./vr_research_data/ --assign P011
"""

import argparse
import os
import random
from pathlib import Path
from datetime import datetime


def scan_existing_orders(quest_data_dir):
    """Scan session folders to find Task1_Condition for each participant."""
    orders = {}
    quest_path = Path(quest_data_dir)

    if not quest_path.exists():
        return orders

    for item in quest_path.iterdir():
        if not item.is_dir():
            continue
        # Folder format: {ParticipantID}_{Timestamp}
        parts = item.name.split('_')
        if len(parts) < 2:
            continue
        pid = parts[0]

        # Check task files to determine condition
        has_task1_computer = False
        has_task1_fan = False
        for f in item.iterdir():
            if f.name.startswith('task_1') and f.name.endswith('.csv'):
                # Check first few rows for task type hints
                pass

        # Check metadata files
        meta_files = list(item.glob('*metadata*'))
        summary_files = list(item.glob('session_summary.json'))

        if pid not in orders:
            orders[pid] = 'Unknown'

        # Try to determine from folder naming or contents
        # Default: look for condition markers
        for f in item.iterdir():
            if 'computer' in f.name.lower() or 'task_a' in f.name.lower():
                orders[pid] = 'A_then_B'
                break
            elif 'fan' in f.name.lower() or 'task_b' in f.name.lower():
                orders[pid] = 'B_then_A'
                break

    return orders


def suggest_next_order(orders):
    """Suggest the next task order to maintain balance."""
    a_then_b = sum(1 for v in orders.values() if v == 'A_then_B')
    b_then_a = sum(1 for v in orders.values() if v == 'B_then_A')
    unknown = sum(1 for v in orders.values() if v == 'Unknown')

    print(f"\nCurrent Balance:")
    print(f"  A→B (Computer first): {a_then_b} participants")
    print(f"  B→A (Fan first):      {b_then_a} participants")
    print(f"  Unknown order:         {unknown} participants")
    print(f"  Total:                 {len(orders)} participants")

    if a_then_b < b_then_a:
        suggested = 'A_then_B'
        reason = f"Need more A→B to balance (diff: {b_then_a - a_then_b})"
    elif b_then_a < a_then_b:
        suggested = 'B_then_A'
        reason = f"Need more B→A to balance (diff: {a_then_b - b_then_a})"
    else:
        suggested = random.choice(['A_then_B', 'B_then_A'])
        reason = "Balanced! Random assignment."

    print(f"\n  ➜ Suggested next order: {suggested}")
    print(f"    Reason: {reason}")

    return suggested


def assign_participant(quest_data_dir, participant_id, order):
    """Create a metadata file recording the assigned order."""
    quest_path = Path(quest_data_dir)
    folder_name = f"{participant_id}_{datetime.now().strftime('%Y%m%d_%H%M%S')}"
    session_dir = quest_path / folder_name
    session_dir.mkdir(parents=True, exist_ok=True)

    assignment = {
        'ParticipantID': participant_id,
        'AssignedOrder': order,
        'Task1_Type': 'Computer Assembly' if order == 'A_then_B' else 'Fan Repair',
        'Task2_Type': 'Fan Repair' if order == 'A_then_B' else 'Computer Assembly',
        'AssignedAt': datetime.now().isoformat(),
    }

    import json
    meta_path = session_dir / 'participant_assignment.json'
    with open(meta_path, 'w') as f:
        json.dump(assignment, f, indent=2)

    print(f"\n  ✅ Assigned {participant_id}: {order}")
    print(f"     Task 1: {assignment['Task1_Type']}")
    print(f"     Task 2: {assignment['Task2_Type']}")
    print(f"     Saved to: {meta_path}")

    return assignment


def main():
    parser = argparse.ArgumentParser(description='VR Research Counterbalance Randomization')
    parser.add_argument('--quest-data-dir', required=True, help='Quest data directory')
    parser.add_argument('--assign', help='Participant ID to assign next order')
    args = parser.parse_args()

    print("=" * 50)
    print("VR RESEARCH COUNTERBALANCE RANDOMIZATION")
    print("=" * 50)

    orders = scan_existing_orders(args.quest_data_dir)

    if not orders:
        print("\nNo existing sessions found. Starting fresh.")
        print("First participant will be randomly assigned.")

    suggested = suggest_next_order(orders)

    if args.assign:
        assign_participant(args.quest_data_dir, args.assign, suggested)
    else:
        print(f"\nTo assign: python counterbalance_randomization.py --quest-data-dir {args.quest_data_dir} --assign P0XX")


if __name__ == '__main__':
    main()
