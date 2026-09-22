const container=document.querySelector('#notices');
window.desktopNotices.onUpdate(({dark,notices,remaining})=>{
  document.documentElement.dataset.theme=dark?'dark':'light';
  const ids=new Set(notices.map(n=>n.Id));
  for(const element of [...container.children])if(!ids.has(element.dataset.id))element.remove();
  for(const [index,n] of notices.entries()){
    let card=[...container.children].find(el=>el.dataset.id===n.Id);
    if(!card){
      card=document.createElement('article');card.className='notice';card.dataset.id=n.Id;
      card.innerHTML='<div class="heading"><img src="icon.png" alt=""><strong>SimpleCommit</strong><small></small></div><p></p><button aria-label="알림 확인" title="확인">×</button>';
      card.querySelector('p').textContent=n.Text;card.querySelector('p').title=n.Text;
      card.querySelector('button').onclick=()=>window.desktopNotices.dismiss(n.Id);
      container.insertBefore(card,container.children[index]||null);
    }
    card.classList.toggle('closing',n.closing);
    card.querySelector('small').textContent=n.preview?'테스트':index===notices.length-1&&remaining?`대기 ${remaining}개`:'';
  }
});
