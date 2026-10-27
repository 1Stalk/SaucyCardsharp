#!/usr/bin/env python3
"""
Triple Triad Open Tournament Simulator & Regression Harness
Parses tournament match logs from XIVLauncher/pluginConfigs/Saucy/UniversalMatchLogs
and evaluates the Universal Engine's solver decisions against recorded matches.
"""

import os
import glob
import re
from dataclasses import dataclass, field
from typing import List, Optional, Tuple, Dict

# True card database stats for Open Tournament draft pool (Up, Left, Down, Right)
KNOWN_CARDS = {
    # Blue Deck
    "Qitian Dasheng": (3, 8, 4, 8, 3),
    "Deepeye": (1, 2, 7, 3, 1),
    "Livia sas Junius": (3, 1, 7, 7, 2),
    "Firion": (10, 1, 10, 5, 5),
    "Endwalker Alphinaud & Alisaie": (4, 3, 9, 9, 4),
    
    # Match 1 Red Deck
    "Coeurl": (2, 2, 5, 5, 2),
    "Artoirel de Fortemps": (4, 7, 6, 2, 3),
    "Bahamut": (9, 6, 9, 6, 5),
    "Quintus van Cinna": (8, 8, 2, 2, 4),
    
    # Match 2 Red Deck
    "Alpaca": (5, 5, 4, 3, 2),
    "Hoary Boulder & Coultenet": (4, 2, 6, 7, 3),
    "Nanamo Ul Namo": (10, 10, 7, 2, 5),
    "Hancock": (8, 6, 1, 7, 4),
    
    # Match 3 Red Deck
    "Lunar Bahamut": (9, 5, 9, 5, 5),
    "Sylph": (2, 3, 2, 5, 1),
    "Ultros & Typhon": (2, 3, 6, 3, 2),
    "Raubahn Aldynn": (6, 2, 3, 8, 4),
    "Elidibus": (7, 1, 6, 7, 3),
}

@dataclass
class Card:
    name: str
    up: int
    left: int
    down: int
    right: int
    rarity: int

    @property
    def sides(self):
        return [self.up, self.left, self.down, self.right]

    def get_side(self, side_idx: int) -> int:
        return self.sides[side_idx]

    def get_opposite(self, side_idx: int) -> int:
        return self.sides[(side_idx + 2) % 4]

def get_card(name: str) -> Card:
    if name in KNOWN_CARDS:
        u, l, d, r, rar = KNOWN_CARDS[name]
        return Card(name, u, l, d, r, rar)
    # Default fallback
    return Card(name, 3, 3, 3, 3, 1)

class Board:
    def __init__(self):
        self.cards: List[Optional[Card]] = [None] * 9
        self.owners: List[str] = ["."] * 9

    def clone(self):
        b = Board()
        b.cards = list(self.cards)
        b.owners = list(self.owners)
        return b

    @staticmethod
    def get_neighbors(pos: int) -> List[int]:
        x, y = pos % 3, pos / 3
        return [
            pos - 3 if y > 0 else -1, # Up (0)
            pos - 1 if x > 0 else -1, # Left (1)
            pos + 3 if y < 2 else -1, # Down (2)
            pos + 1 if x < 2 else -1, # Right (3)
        ]

    def place_card(self, card: Card, pos: int, owner: str) -> List[int]:
        self.cards[pos] = card
        self.owners[pos] = owner
        flips = []

        neis = self.get_neighbors(pos)
        for side in range(4):
            nei_pos = neis[side]
            if nei_pos >= 0 and self.cards[nei_pos] is not None:
                nei_owner = self.owners[nei_pos]
                if nei_owner != owner:
                    my_power = card.get_side(side)
                    opp_power = self.cards[nei_pos].get_opposite(side)
                    if my_power > opp_power:
                        self.owners[nei_pos] = owner
                        flips.append(nei_pos)
        return flips

    def score(self, blue_in_hand: int, red_in_hand: int) -> Tuple[int, int]:
        blue = blue_in_hand + sum(1 for o in self.owners if o == "Blue")
        red = red_in_hand + sum(1 for o in self.owners if o == "Red")
        return blue, red

def evaluate_positional_safety(card: Card, pos: int, placed_count: int) -> float:
    score = 0.0
    is_corner = pos in (0, 2, 6, 8)
    is_edge = pos in (1, 3, 5, 7)

    if is_corner:
        score += 0.55 if placed_count <= 1 else 0.40
    elif is_edge:
        score += 0.20
    elif pos == 4 and placed_count <= 1:
        score -= 0.30

    neis = Board.get_neighbors(pos)
    exposed_count = 0
    exposed_power_sum = 0

    for s in range(4):
        nei_pos = neis[s]
        num = card.sides[s]
        if nei_pos < 0:
            if num <= 4:
                score += 0.12
        else:
            exposed_count += 1
            exposed_power_sum += num
            if num >= 9:
                score += 0.18
            elif num >= 7:
                score += 0.08
            elif num <= 3:
                score -= 0.25
            elif num <= 5:
                score -= 0.10

    if exposed_count > 0:
        score += (exposed_power_sum / (exposed_count * 10.0)) * 0.30

    # Rarity Pacing
    if placed_count <= 1:
        all_fortified = True
        for s in range(4):
            if neis[s] >= 0:
                if card.sides[s] < 9:
                    all_fortified = False
        if card.rarity == 5 and not all_fortified:
            score -= 0.35
        elif card.rarity == 1:
            score += 0.18
        elif card.rarity == 2:
            score += 0.10

    return max(0.0, score)

def minimax(board: Board, blue_hand: List[Card], red_hand: List[Card], is_blue: bool, depth: int, alpha: float, beta: float) -> float:
    b_score, r_score = board.score(len(blue_hand), len(red_hand))
    if all(c is not None for c in board.cards) or depth == 0:
        if b_score > r_score:
            return 1.0
        elif b_score < r_score:
            return 0.0
        return 0.5

    hand = blue_hand if is_blue else red_hand
    if is_blue:
        max_val = 0.0
        for i, card in enumerate(hand):
            for p in range(9):
                if board.cards[p] is not None:
                    continue
                next_b = board.clone()
                next_blue = blue_hand[:i] + blue_hand[i+1:]
                next_b.place_card(card, p, "Blue")
                val = minimax(next_b, next_blue, red_hand, False, depth - 1, alpha, beta)
                if val > max_val:
                    max_val = val
                if val > alpha:
                    alpha = val
                if alpha >= beta:
                    return max_val
        return max_val
    else:
        min_val = 1.0
        for i, card in enumerate(hand):
            for p in range(9):
                if board.cards[p] is not None:
                    continue
                next_b = board.clone()
                next_red = red_hand[:i] + red_hand[i+1:]
                next_b.place_card(card, p, "Red")
                val = minimax(next_b, blue_hand, next_red, True, depth - 1, alpha, beta)
                if val < min_val:
                    min_val = val
                if val < beta:
                    beta = val
                if beta <= alpha:
                    return min_val
        return min_val

def get_best_move(board: Board, hand: List[Card], opp_hand: List[Card], is_blue: bool, placed_count: int, depth: int = 6):
    best_card = None
    best_pos = -1
    best_score = -999.0
    candidates = []

    for i, card in enumerate(hand):
        for p in range(9):
            if board.cards[p] is not None:
                continue
            next_b = board.clone()
            next_hand = hand[:i] + hand[i+1:]
            owner = "Blue" if is_blue else "Red"
            next_b.place_card(card, p, owner)

            val = minimax(next_b, next_hand if is_blue else opp_hand, opp_hand if is_blue else next_hand, not is_blue, depth - 1, 0.0, 1.0)
            pos_score = evaluate_positional_safety(card, p, placed_count)
            total = (val * 10.0) + pos_score

            candidates.append((card, p, val, pos_score, total))
            if total > best_score:
                best_score = total
                best_card = card
                best_pos = p

    return best_card, best_pos, best_score, candidates

def parse_log(filepath: str):
    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    plies = []
    # Match plies: PLY N: Side
    ply_blocks = re.split(r"--------------------------------------------------------------------------------\nPLY\s+(\d+):\s+(\w+)", content)
    for i in range(1, len(ply_blocks), 3):
        ply_num = int(ply_blocks[i])
        side = ply_blocks[i+1].split()[0]
        block = ply_blocks[i+2]
        
        m_action = re.search(r"Placed '([^']+)' at Tile \((\d+),\s*(\d+)\) \[Slot (\d+)\]", block)
        if m_action:
            card_name = m_action.group(1)
            slot = int(m_action.group(4))
            plies.append((ply_num, side, card_name, slot))
    
    # Final result
    m_res = re.search(r"FINAL RESULT:\s*([^\n]+)", content)
    final_result = m_res.group(1) if m_res else "Unknown"

    return plies, final_result

def simulate_log(filepath: str):
    filename = os.path.basename(filepath)
    plies, original_result = parse_log(filepath)
    if not plies:
        return

    print("=" * 80)
    print(f"ANALYZING MATCH LOG: {filename}")
    print(f"Recorded Result in Tournament: {original_result}")
    print("-" * 80)

    # Blue initial hand
    blue_deck = [
        get_card("Qitian Dasheng"),
        get_card("Deepeye"),
        get_card("Livia sas Junius"),
        get_card("Firion"),
        get_card("Endwalker Alphinaud & Alisaie")
    ]

    # Extract opponent moves from the log
    red_moves = [(card, slot) for (_, side, card, slot) in plies if side == "Red"]
    red_deck = [get_card(c) for (c, _) in red_moves]
    # Add 5th unplayed card if needed
    if len(red_deck) < 5:
        red_deck.append(get_card("Coblyn"))

    # Replay simulation: Blue uses corrected solver, Red executes the exact human moves recorded in the log!
    board = Board()
    sim_blue_hand = list(blue_deck)
    sim_red_hand = list(red_deck)
    red_idx = 0
    is_blue_turn = (plies[0][1] == "Blue")

    print("\nReplay Simulation with Corrected Universal Engine:")
    for ply in range(1, 10):
        if None not in board.cards:
            break
        placed_count = sum(1 for c in board.cards if c is not None)
        if is_blue_turn:
            if not sim_blue_hand:
                break
            depth = 10 if placed_count >= 4 else (4 if placed_count <= 1 else 6)
            b_card, b_pos, score, _ = get_best_move(board, sim_blue_hand, sim_red_hand, True, placed_count, depth=depth)
            sim_blue_hand.remove(b_card)
            flips = board.place_card(b_card, b_pos, "Blue")
            flip_str = f" [Captured {len(flips)} card(s)]" if flips else ""
            print(f"  Ply {ply}: Blue (Bot)   placed '{b_card.name}' (*{b_card.rarity}) at Tile ({b_pos%3}, {b_pos//3}) [Slot {b_pos}]{flip_str}")
        else:
            if not sim_red_hand:
                break
            if red_idx < len(red_moves):
                r_card_name, r_pos = red_moves[red_idx]
                red_idx += 1
                r_card = next((c for c in sim_red_hand if c.name == r_card_name), sim_red_hand[0])
                if board.cards[r_pos] is not None:
                    # Slot taken, fallback to first empty
                    r_pos = board.cards.index(None)
                if r_card not in sim_red_hand:
                    r_card = sim_red_hand[0]
            else:
                r_card = sim_red_hand[0]
                r_pos = board.cards.index(None)

            sim_red_hand.remove(r_card)
            flips = board.place_card(r_card, r_pos, "Red")
            flip_str = f" [Captured {len(flips)} card(s)]" if flips else ""
            print(f"  Ply {ply}: Red  (Human) placed '{r_card.name}' (*{r_card.rarity}) at Tile ({r_pos%3}, {r_pos//3}) [Slot {r_pos}]{flip_str}")

        is_blue_turn = not is_blue_turn

    b_final, r_final = board.score(len(sim_blue_hand), len(sim_red_hand))
    outcome = "VICTORY (WIN!)" if b_final > r_final else ("DRAW" if b_final == r_final else "DEFEAT")
    print(f"\nFinal Result: Blue {b_final} vs Red {r_final} => {outcome}")
    print("=" * 80 + "\n")

def main():
    logs_dir = os.path.join(
        os.environ.get("APPDATA", ""),
        "XIVLauncher", "pluginConfigs", "Saucy", "UniversalMatchLogs"
    )
    
    if not os.path.exists(logs_dir):
        print(f"Logs directory not found: {logs_dir}")
        return

    log_files = sorted(glob.glob(os.path.join(logs_dir, "match_*.log")))
    if not log_files:
        print(f"No match logs found in {logs_dir}")
        return

    print(f"Found {len(log_files)} tournament logs. Running analysis...")
    for f in log_files:
        simulate_log(f)

if __name__ == "__main__":
    main()
