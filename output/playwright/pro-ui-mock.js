async (page) => {
  const today = new Date(); today.setHours(0,0,0,0); const monday = new Date(today); monday.setDate(monday.getDate() - (monday.getDay()+6)%7);
  const iso = (days, hours = 0) => new Date(today.getTime() + days * 86400000 + hours * 3600000).toISOString();
  const names = ['district','memory','ephemeral','ambiguous','journey','recreation','garden','river'];
  const buckets = ['overdue','today','tomorrow','week','fortnight','month','later','unscheduled'];
  const labels = ['已到期','今天','明天','2～6天','7～13天','14～30天','31天+','未排期'];
  const offsets = [-2,0,1,3,8,18,40,null];
  const words = Array.from({length:62}, (_,i) => ({
    key:`CET4_1:${i+1}`, bookId:'CET4_1', bookName:'四级核心词汇', headWord:i<8?names[i]:`word-${i+1}`,
    translation:i===0?'n. 区；区域；行政区':`第 ${i+1} 个词的释义`, status:5,
    bucket:buckets[i%8], dueUtc:offsets[i%8]===null?null:iso(offsets[i%8],i%8===1?23:12), starred:i%5===0?1:0, wrongCount:i%4===0?2:0
  }));
  const settings = {BookId:'CET4_1',Number:10,EngType:2,AutoPlay:false,AutoLog:true};
  const books=[{id:'CET4_1',name:'四级核心词汇',personal:0,total:62,learned:62,reviewed:62},{id:'personal_demo',name:'空的个人词库',personal:1,total:0,learned:0,reviewed:0}];
  let session=null;
  const counts={};
  page.__tfMock={counts,words,settings};
  const events=Array.from({length:75},(_,i)=>({id:`e-${i}`,atUtc:iso(0,9),key:words[i%62].key,headWord:words[i%62].headWord,translation:words[i%62].translation,bookName:'四级核心词汇',kind:i%3===0?'test':i%3===1?'new':'review',action:i%3===0?'0':i%2?'good':'again',correct:i%3===0?(i%2):null,sessionId:'round-demo'}));
  await page.route('**/api/**',async route=>{
    const request=route.request(),url=new URL(request.url()),p=url.pathname.replace('/api/',''),q=url.searchParams;
    const method=request.method();counts[method+' '+p]=(counts[method+' '+p]||0)+1;
    const reply=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
    if(!request.headers().authorization?.includes('ui-fixture')) return reply({error:'管理页凭据已失效，请从托盘重新打开。'},401);
    if(method==='POST'){
      const body=request.postDataJSON()||{};
      await new Promise(resolve=>setTimeout(resolve,120));
      if(p==='sessions/start'){ if(session)return reply({error:'请先继续或结束未完成的学习。'},409);session={id:'ui-round',bookId:body.bookId,state:'running',stage:'learn',total:body.number,rated:3,finished:1,current:'district',remainingTests:0};return reply({ok:true,session}); }
      if(p==='sessions/resume'){if(!session)return reply({error:'没有未完成的学习。'},409);session.state='running';return reply({ok:true});}
      if(p==='sessions/pause'){if(session)session.state='paused';return reply({ok:true});}
      if(p==='sessions/end'){session=null;return reply({ok:true});}
      if(p==='words/star'){const w=words.find(w=>w.key===body.key);if(w)w.starred=body.starred?1:0;return reply({ok:true});}
      if(p==='settings'){Object.assign(settings,body);return reply({ok:true});}
      if(p==='imports/preview')return reply({count:2,items:[{wordRank:1,headWord:'sample',tranCN:'例子',pos:'n.'},{wordRank:2,headWord:'practice',tranCN:'练习',pos:'n.'}]});
      if(p==='imports/confirm')return reply({bookId:'personal_mock'});
      if(p==='books/reset'||p.startsWith('backups/'))return reply({ok:true});
      return reply({error:'未模拟的接口，不会转发到真实后端。'},404);
    }
    if(p==='sessions')return reply(session);
    if(p==='settings')return reply(settings);
    if(p==='books')return reply(books);
    if(p==='dashboard')return reply({today:{newCount:16,reviewCount:28,actions:48},week:{newCount:46,reviewCount:80,actions:218},due:8,historySince:iso(-7),dataPath:'本地用户数据目录（浏览器测试使用内存模拟）',session,settings,books,days:Array.from({length:7},(_,i)=>({date:new Date(monday.getTime()+i*86400000).toISOString().slice(0,10),counts:{newCount:[12,16,8,10,0,0,0][i],reviewCount:[19,28,23,14,0,0,0][i]}}))});
    if(p==='reviews/overview')return reply({algorithm:'SM2plus',generatedAt:new Date().toISOString(),total:q.get('book')==='personal_demo'?0:62,buckets:buckets.map((id,i)=>({id,label:labels[i],count:words.filter(w=>w.bucket===id&&(!q.get('book')||w.bookId===q.get('book'))).length}))});
    if(p==='words'){
      let list=words.filter(w=>(!q.get('book')||w.bookId===q.get('book'))&&(!q.get('q')||(w.headWord+' '+w.translation).toLowerCase().includes(q.get('q').toLowerCase()))&&(!q.get('window')||w.bucket===q.get('window')));
      if(q.get('filter')==='due')list=list.filter(w=>w.bucket==='overdue');
      if(q.get('filter')==='wrong')list=list.filter(w=>w.wrongCount);
      if(q.get('filter')==='starred')list=list.filter(w=>w.starred);
      if(q.get('filter')==='forgotten')list=list.filter((w,i)=>i%4===0);
      if(q.get('filter')==='new'||q.get('filter')==='learning')list=[];
      const n=Number(q.get('page')||1);return reply({items:list.slice((n-1)*50,n*50),count:list.length,page:n,pageSize:50});
    }
    if(p==='words/detail'){
      const w=words.find(w=>w.key===q.get('key'));if(!w)return reply({error:'找不到单词。'},400);
      return reply({starred:!!w.starred,wrongCount:w.wrongCount,word:{Key:w.key,Status:w.status,Difficulty:.34,Interval:3,DueUtc:w.dueUtc,Word:{headWord:w.headWord,pos:'n.',tranCN:w.translation,ukPhone:'ˈdɪstrɪkt',usPhone:'ˈdɪstrɪkt',phrase:'a business district',phraseCN:'商业区',sentence:'The district is known for its quiet streets.',sentenceCN:'这个地区以安静的街道而闻名。'}},events:events.filter(e=>e.key===w.key).slice(0,4)});
    }
    if(p==='records'){
      let list=events;if(q.get('kind')==='test')list=list.filter(e=>e.kind==='test');if(q.get('kind')==='ratings')list=list.filter(e=>e.kind!=='test');
      const n=Number(q.get('page')||1);return reply({items:list.slice((n-1)*50,n*50),count:list.length,page:n,pageSize:50,sessions:[{id:'round-demo',bookId:'CET4_1',bookName:'四级核心词汇',state:'completed',mode:'normal',created:iso(0,8),completed:iso(0,10)},{id:'paused-round',bookId:'CET4_1',bookName:'四级核心词汇',state:'paused',created:iso(-1,8),completed:null}]});
    }
    if(p==='sessions/detail')return reply({round:{State:q.get('id')==='paused-round'?'paused':'completed',Created:iso(0,8),Cards:Object.fromEntries(words.slice(0,10).map(w=>[w.key,{Key:w.key,Status:w.status,Rated:true,Word:{headWord:w.headWord,tranCN:w.translation}}])),SkippedTests:0},events:events.slice(0,15)});
    if(p==='exports'||p==='books/export'||p==='backups')return route.fulfill({status:200,contentType:'application/octet-stream',headers:{'content-disposition':'attachment; filename="mock-download.bin"'},body:Buffer.from('In-memory UI test download; no database accessed.')});
    return reply({error:'未模拟的接口，不会转发到真实后端。'},404);
  });
  await page.goto('http://127.0.0.1:4173/#token=ui-fixture');
  await page.reload(); await page.getByText('本周新学',{exact:false}).waitFor();
}
