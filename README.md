# Unity-GPU-Culling

测试例：在整个场景中有150\*150\*1=22500个待渲染物体。在未经任何优化与剔除的情况下，渲染效果如图。可以看到帧率甚至达不到30FPS。

![image-20210810192409553](https://ruin-typora.oss-cn-beijing.aliyuncs.com/image-20210810192409553.png)

进行视锥剔除后的效果如下。帧率可以基本达到60FPS。

![image-20210810192516093](https://ruin-typora.oss-cn-beijing.aliyuncs.com/image-20210810192516093.png)

进行视锥剔除+基于HZB的遮挡剔除后效果如下。由于每帧都在检测与调用，所以在浪费一定开销的损失下，保证了渲染的正确以及稳定，帧率可以稳定在70~80FPS。对于DrawCall小的情况，帧率可以超过100FPS。

![image-20210810192551998](https://ruin-typora.oss-cn-beijing.aliyuncs.com/image-20210810192551998.png)

![image-20210810192816060](https://ruin-typora.oss-cn-beijing.aliyuncs.com/image-20210810192816060.png)