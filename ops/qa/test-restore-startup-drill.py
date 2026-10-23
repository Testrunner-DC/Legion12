"""Pure archived-schema compatibility checks. No network, runtime or output files."""
import ast
import copy
import importlib.util
from pathlib import Path
import sys

sys.dont_write_bytecode = True
source = Path(__file__).with_name('restore-startup-drill.py')
spec = importlib.util.spec_from_file_location('restore_drill_pure_test', source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
base = [{'Id': 'synthetic-only', 'Other': 7}]
untouched = copy.deepcopy(base)
hash_accounts = lambda value: module.collection_hash('Accounts', value)
assert hash_accounts(base) == hash_accounts([dict(base[0], FontSize='standard')])
assert base == untouched
for explicit in ('large', 'small', '', None):
    assert hash_accounts(base) != hash_accounts([dict(base[0], FontSize=explicit)])
assert hash_accounts(base) != hash_accounts([dict(base[0], Other=8)])
assert module.collection_hash('Other', base) != module.collection_hash('Other', [dict(base[0], FontSize='standard')])
assert hash_accounts(base) != hash_accounts(base + base)
# Inspect the actual child's environment literal without running copy/startup.
tree = ast.parse(source.read_text(encoding='utf-8'))
business_keys = [ast.literal_eval(node.iter) for node in ast.walk(tree) if isinstance(node, ast.For)
                 and isinstance(node.target, ast.Name) and node.target.id == 'key' and isinstance(node.iter, ast.Tuple)]
assert business_keys == [('Accounts', 'Friends', 'BugReports', 'Articles', 'SiteMedia')]
environment = [node.value for node in ast.walk(tree) if isinstance(node, ast.Assign)
               and any(isinstance(name, ast.Name) and name.id == 'environment' for name in node.targets)]
assert len(environment) == 1 and isinstance(environment[0], ast.Dict)
keys = [ast.literal_eval(key) for key in environment[0].keys]
assert len(keys) == len(set(keys))
assert 'L12_TESTRUN_MATCH_STORAGE' not in keys
values = dict(zip(keys, environment[0].values))
assert ast.literal_eval(values['L12_LISTEN_HOST']) == '127.0.0.1'
assert ast.literal_eval(values['L12_PRIVATE_DECK_OBJECT_PERSISTENCE']) == 'true'
assert module.ROOT.parent == Path('/tmp')
print('Restore drill pure compatibility/boundary checks passed: 16; network=0, application starts=0, output files=0')
