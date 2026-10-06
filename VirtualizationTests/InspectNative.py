import pathlib,struct,bisect,re,capstone,pefile
src=pathlib.Path(r'E:\aamod\diagnostics\ilspy-full2'); root=pathlib.Path(r'F:\AzureArchive_100_fix'); out=pathlib.Path(r'E:\aamod\Azurite-work\virtualization-1.0.2\VirtualizationTests\native.txt')
b=(root/'GameAssembly.dll').read_bytes(); pe=pefile.PE(str(root/'GameAssembly.dll'),fast_load=True); base=pe.OPTIONAL_HEADER.ImageBase
mods=[]; pos=0
while 1:
 pos=b.find(b'Assembly-CSharp.dll\0',pos)
 if pos<0:break
 ptr=struct.pack('<Q',base+pe.get_rva_from_offset(pos)); start=0
 while 1:
  start=b.find(ptr,start)
  if start<0:break
  vals=struct.unpack_from('<4Q',b,start)
  if 20000<vals[1]<100000:mods.append(vals)
  start+=1
 pos+=1
assert len(mods)==1
v=mods[0]; arr=struct.unpack_from(f'<{v[1]}Q',b,pe.get_offset_from_rva(v[2]-base)); starts=sorted({a for a in arr if a}); md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
names={}; targets={}
for f in src.rglob('*.cs'):
 for name,tok in re.findall(r'NativeMethodInfoPtr_(\w+) = IL2CPP.GetIl2CppMethodByToken\([^\n]*, (\d+)\)',f.read_text(encoding='utf-8-sig')):
  t=int(tok); names.setdefault(arr[(t&0xffffff)-1],[]).append(f.stem+'.'+name)
  if f.stem in ('ScriptNodeInspector','ScriptListItem','Selectable','CenterableUIScrollView','UIScrollView','UIGrid','NGUIMath','Util','ScenarioScriptAddOperation','ScenarioScriptDeleteOperation','ScriptNodeInspectorStateChangeOperation','ScenarioScriptInspectorModifyOperation'): targets[t]=f.stem+'.'+name
lines=[]
for tok,name in targets.items():
 a=arr[(tok&0xffffff)-1]; end=starts[bisect.bisect_right(starts,a)]; lines.append(f'\nMETHOD {name} token={tok} RVA={a-base:08x}')
 for ins in md.disasm(pe.get_data(a-base,end-a),a):
  label=''
  if ins.mnemonic in ('call','jmp') and ins.op_str.startswith('0x'):label=' '+','.join(names.get(int(ins.op_str,16),[]))
  lines.append(f'{ins.address-base:08x}: {ins.mnemonic:7} {ins.op_str}{label}')
out.write_text('\n'.join(lines),encoding='utf-8'); print(out)

