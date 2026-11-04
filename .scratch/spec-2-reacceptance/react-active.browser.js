// 在 pi 的 browser 工具中执行本文件内容；需要真实 API :5079 与 React :5174。
// 只通过公开 UI 导入/执行，读取渲染 DOM；不注入 store 或伪造 SignalR。
const script = `module M { input Entry, Retry; output Done;
 def Entry(Idle) { 1->Done; Pos:(100,100); }
 def Retry(Idle) { 1->Done; Pos:(300,100); }
}
use M as A { Pos:(400,50); Color:"purple"; }
def Start(Start) { 1->Launch; Pos:(80,300); Color:"red"; }
def A.Observer(Idle) { 1->Observe; Pos:(400,300); Color:"orange"; }
def End(End) { 1->EndEvent; Pos:(800,300); Color:"blue"; }
Launch -> Start to A.Observer;
Observe -> A.Observer to End;
A.Done -> End;`;
if (!page.url().startsWith('http://127.0.0.1:5174/')) throw new Error('请先导航到 React :5174');
const stop = page.getByRole('button', { name: 'stop Stop', exact: true });
if (await stop.isEnabled()) await human.click(stop, { note: '停止上次执行' });
const results = [];
for (const target of ['A.Observer', 'A.Retry']) {
  await human.click(page.getByRole('button', { name: 'import Import', exact: true }), { note: '导入身份回归流程' });
  const dialog = page.getByRole('dialog', { name: 'Import FSM script', exact: true });
  await dialog.getByRole('textbox').fill(script.replace('Launch -> Start to A.Observer;', `Launch -> Start to ${target};`));
  await human.click(dialog.getByRole('button', { name: 'Import script', exact: true }), { note: '构建真实流程' });
  await dialog.waitFor({ state: 'hidden' });
  await human.click(page.getByRole('button', { name: 'Fit View', exact: true }), { note: '适配可见画布' });
  await human.click(page.getByRole('button', { name: 'play-circle Start', exact: true }), { note: '执行流程' });
  try {
    await page.getByRole('banner').getByText(new RegExp(`Enter ${target.replace('.', '\\.')}`)).waitFor({ state: 'visible', timeout: 15000 });
    await human.click(page.getByRole('button', { name: 'pause-circle Pause', exact: true }), { note: '暂停于目标节点' });
    const moduleStyle = await page.getByText('📦 A', { exact: true }).locator('..').getAttribute('style');
    const ordinaryStyle = await page.getByRole('application').getByText('A.Observer', { exact: true }).locator('..').getAttribute('style');
    if (moduleStyle.includes('fsm-active-node-pulse') !== (target === 'A.Retry')) throw new Error(`模块活动归属错误：${target}`);
    if (ordinaryStyle.includes('fsm-active-node-pulse') !== (target === 'A.Observer')) throw new Error(`普通节点活动归属错误：${target}`);
    const edge = page.getByRole('group', { name: `Edge from Start to ${target === 'A.Retry' ? 'A' : target}`, exact: true });
    if (!(await edge.getAttribute('class')).includes('fsm-active-edge')) throw new Error('活动转移折叠错误');
    results.push(`${target}: Root PASS`);
  } finally {
    if (await stop.isEnabled()) await human.click(stop, { note: '清理回归执行' });
  }
}
return results;
