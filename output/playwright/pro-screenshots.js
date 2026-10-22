async page => {
  await page.setViewportSize({width:1600,height:1050});
  const shots=[
    ['学习首页 Overview','home',()=>page.getByText('本周新学',{exact:false}).waitFor()],
    ['学习记录 History','records',()=>page.getByRole('button',{name:'轮次复盘 →'}).first().waitFor()],
    ['我的单词 Vocabulary','words',()=>page.getByRole('heading',{name:'district',exact:true}).waitFor()],
    ['复习与错词 Review','reviews',()=>page.getByRole('button',{name:'明天，8个词',exact:true}).waitFor()],
    ['词库与设置 Settings','settings',()=>page.getByRole('button',{name:'保存设置',exact:true}).waitFor()]
  ];
  for(const [name,file,ready] of shots){await page.getByRole('link',{name}).click();await ready();await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:`output/playwright/pro-${file}.png`,scale:'css'});}
  await page.setViewportSize({width:390,height:844});await page.getByRole('link',{name:/复习与错词/}).click();await page.getByRole('button',{name:'明天，8个词',exact:true}).waitFor();await page.screenshot({path:'output/playwright/pro-reviews-mobile.png',scale:'css'});
  return {screenshots:6,simulatedData:true,databaseAccess:false};
}
