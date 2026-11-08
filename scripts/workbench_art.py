"""Translate the reviewed operator-facing art names to RimWorld building rotations."""
from pathlib import Path

# These complete sprites are authored by the edge a pawn approaches. The Def's
# (0,0,-1) interaction cell points in the direction opposite its Rotation.
OPERATOR_SIDE = {'north': 'south', 'east': 'west', 'south': 'north', 'west': 'east'}
FAMILIES = {
    'GrechkaIndustry': tuple(f'AssemblyBenchT{tier}' for tier in range(1, 6)) + ('MatterRecyclerT4',),
    'GrechkaStorage': ('ElectricInscriber',),
}


def directional_sources(module, source):
    """Runtime-path -> original-path. Other machines keep the normal identity mapping."""
    result = {}
    for name in FAMILIES.get(module, ()):
        directory = Path('Buildings/Map') / name
        for rotation, operator in OPERATOR_SIDE.items():
            result[directory / f'{name}_{rotation}.png'] = directory / f'{name}_{operator}.png'
    if module == 'GrechkaIndustry':
        # This batch calls the original west view simply Off / Idle / Active.
        directory = Path('Buildings/Map/States/MatterRecyclerT4')
        for state in ('Off', 'Idle', 'Active'):
            stem = 'MatterRecyclerT4_' + state
            for rotation, operator in OPERATOR_SIDE.items():
                file = stem + '_' + operator
                if operator == 'west' and not (source / directory / (file + '.png')).is_file():
                    file = stem
                result[directory / f'{stem}_{rotation}.png'] = directory / (file + '.png')
    return result
